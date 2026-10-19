using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;
using KK.Var.Enums;
using KK.Var.Models;
using Microsoft.EntityFrameworkCore;

namespace KK.Var.Services.Implementations;

public sealed class RemoteMachineService(
    IDbContextFactory<AppDbContext> contextFactory,
    ISshPasswordStore passwordStore,
    IDeploymentOperationQueue queue,
    ILocalizationService localization) : IRemoteMachineService
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly Dictionary<Guid, int> _reservations = [];

    public async Task<IReadOnlyList<RemoteMachine>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RemoteMachines.AsNoTracking().Include(machine => machine.Projects)
            .OrderBy(machine => machine.Name).ToListAsync(cancellationToken);
    }

    public async Task<RemoteMachine> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RemoteMachines.AsNoTracking().SingleOrDefaultAsync(machine => machine.Id == id, cancellationToken) ??
            throw Error("Выберите профиль удалённой машины.");
    }

    public async Task<RemoteMachineSettings> GetConnectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var machine = await GetAsync(id, cancellationToken);
        if (machine.IsArchived || !machine.IsVerified)
        {
            throw Error("Профиль машины должен быть проверен и не архивирован.");
        }
        return new RemoteMachineSettings
        {
            Host = machine.Host,
            Port = machine.Port,
            UserName = machine.UserName,
            AuthenticationMethod = machine.AuthenticationMethod,
            PrivateKeyPath = machine.PrivateKeyPath,
            Password = machine.AuthenticationMethod == "Пароль" ? await passwordStore.LoadAsync(id, cancellationToken) : null,
            HostKeyFingerprint = machine.HostKeyFingerprint,
            HostKeyHost = machine.Host,
            HostKeyPort = machine.Port,
            Architecture = machine.Architecture,
        };
    }

    public async Task AssertEditableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await AssertEditableCoreAsync(db, id, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AssertEditableCoreAsync(AppDbContext db, Guid id, CancellationToken cancellationToken)
    {
        if (_reservations.GetValueOrDefault(id) > 0 || queue.HasActiveOperationForMachine(id) ||
            await db.ProjectDeployments.AnyAsync(deployment => deployment.RemoteMachineId == id &&
                (deployment.Status == DeploymentStatus.Running || deployment.Status == DeploymentStatus.Pending), cancellationToken))
        {
            throw Error("Машина занята операцией. Изменение и архивирование недоступны.");
        }
    }

    public async Task<RemoteMachine> SaveVerifiedAsync(RemoteMachine candidate, RemoteMachineSettings connection,
        RemoteConnectionCheckResult verification, DateTime? expectedUpdatedAt, bool identityConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Id == Guid.Empty || !verification.IsSuccessful || string.IsNullOrWhiteSpace(verification.Architecture) ||
            string.IsNullOrWhiteSpace(verification.HostKeyFingerprint) ||
            verification.HostKeyFingerprint != connection.HostKeyFingerprint ||
            string.IsNullOrWhiteSpace(candidate.Name) || candidate.Name.Length > 200 || candidate.Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.UserName) ||
            connection.Port is < 1 or > 65535)
        {
            throw Error("Заполните профиль и успешно проверьте SSH-подключение.");
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await AssertEditableCoreAsync(db, candidate.Id, cancellationToken);
            var current = await db.RemoteMachines.SingleOrDefaultAsync(machine => machine.Id == candidate.Id, cancellationToken);
            if (current?.UpdatedAtUtc != expectedUpdatedAt || current?.IsArchived == true)
            {
                throw Error("Профиль изменился. Откройте его заново.");
            }
            var identityChanged = current is not null && (current.Host != connection.Host?.Trim() ||
                current.Port != connection.Port || current.HostKeyFingerprint != verification.HostKeyFingerprint);
            if (identityChanged && !identityConfirmed)
            {
                throw Error("Подтвердите, является ли новый адрес той же машиной.");
            }
            if (current is null)
            {
                current = new RemoteMachine { Id = candidate.Id };
                db.RemoteMachines.Add(current);
            }
            if ((!string.IsNullOrWhiteSpace(current.HostKeyFingerprint) &&
                    current.HostKeyFingerprint != verification.HostKeyFingerprint) ||
                (!string.IsNullOrWhiteSpace(current.Architecture) &&
                    ArchitectureCompatibility.Normalize(current.Architecture) != ArchitectureCompatibility.Normalize(verification.Architecture)))
            {
                current.ServerGeneration++;
            }
            current.Name = candidate.Name.Trim();
            current.Host = connection.Host!.Trim();
            current.Port = connection.Port;
            current.UserName = connection.UserName!.Trim();
            current.AuthenticationMethod = connection.AuthenticationMethod;
            current.PrivateKeyPath = connection.PrivateKeyPath;
            current.HostKeyFingerprint = verification.HostKeyFingerprint;
            current.Architecture = ArchitectureCompatibility.Normalize(verification.Architecture);
            current.IsVerified = true;
            current.LastVerifiedAtUtc = DateTime.UtcNow;
            current.UpdatedAtUtc = DateTime.UtcNow;
            var previousPassword = await passwordStore.LoadAsync(current.Id, cancellationToken);
            try
            {
                if (current.AuthenticationMethod == "Пароль")
                {
                    var password = connection.Password ?? previousPassword;
                    if (string.IsNullOrWhiteSpace(password))
                    {
                        throw Error("Введите пароль SSH для этого профиля.");
                    }
                    await passwordStore.SaveAsync(current.Id, password, cancellationToken);
                }
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                if (previousPassword is null)
                {
                    await passwordStore.DeleteAsync(current.Id, CancellationToken.None);
                }
                else
                {
                    await passwordStore.SaveAsync(current.Id, previousPassword, CancellationToken.None);
                }
                throw;
            }
            if (current.AuthenticationMethod != "Пароль")
            {
                await passwordStore.DeleteAsync(current.Id, cancellationToken);
            }
            return current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await AssertEditableCoreAsync(db, id, cancellationToken);
            if (await db.Projects.AnyAsync(project => project.RemoteMachineId == id, cancellationToken))
            {
                throw Error("Сначала назначьте проектам этого профиля другую машину.");
            }
            var machine = await db.RemoteMachines.SingleAsync(item => item.Id == id, cancellationToken);
            machine.IsArchived = true;
            machine.IsVerified = false;
            machine.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await passwordStore.DeleteAsync(id, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await AssertEditableCoreAsync(db, id, cancellationToken);
            var machine = await db.RemoteMachines.SingleAsync(item => item.Id == id, cancellationToken);
            await passwordStore.DeleteAsync(id, cancellationToken);
            machine.IsArchived = false;
            machine.IsVerified = false;
            machine.HostKeyFingerprint = null;
            machine.ServerGeneration++;
            machine.LastVerifiedAtUtc = null;
            machine.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IAsyncDisposable> ReserveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var machine = await GetAsync(id, cancellationToken);
            if (!machine.IsVerified || machine.IsArchived)
            {
                throw Error("Профиль машины должен быть проверен и не архивирован.");
            }
            _reservations[id] = _reservations.GetValueOrDefault(id) + 1;
            return new Reservation(this, id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class Reservation(RemoteMachineService owner, Guid id) : IAsyncDisposable
    {
        private bool _disposed;
        public async ValueTask DisposeAsync()
        {
            await owner._gate.WaitAsync();
            try
            {
                if (!_disposed)
                {
                    _disposed = true;
                    owner._reservations[id]--;
                }
            }
            finally
            {
                owner._gate.Release();
            }
        }
    }

    private InvalidOperationException Error(string message) => new InvalidOperationException(localization.Get(message));
}
