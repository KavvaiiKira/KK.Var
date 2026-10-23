using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using KK.Var.Constants;
using KK.Var.Models;
using Microsoft.Extensions.Logging;

namespace KK.Var.Services.Implementations
{
    public sealed class WindowsDeploymentNotifications : IDisposable
    {
        private const string CommonControlsLibrary = "comctl32.dll";

        private const string ShellLibrary = "shell32.dll";

        private const string UserLibrary = "user32.dll";

        private const string NotifyIconEntryPoint = "Shell_NotifyIconW";

        private const string LoadIconEntryPoint = "LoadIconW";

        private const string ExtractIconEntryPoint = "ExtractIconW";

        private const string InitializationFailureMessage = "Could not initialize Windows deployment notifications.";

        private const string DisplayFailureMessage = "Could not display Windows deployment notification.";

        private const string ResourceReleaseFailureMessage = "Could not release Windows deployment notification resources.";

        private const string CallbackFailureMessage = "Windows deployment notification callback failed.";

        private const string RemovalFailureMessage = "Could not remove Windows deployment notification.";

        private const uint CallbackMessage = 0x8000 + 213;

        private readonly ILogger<WindowsDeploymentNotifications> _logger;

        private readonly nint _window;

        private readonly SubclassCallback _callback;

        private readonly Action<DeploymentCompletionNotice> _activated;

        private readonly Dictionary<uint, DeploymentCompletionNotice> _pending = [];

        private uint _nextId;

        private bool _available;

        private bool _disposed;

        private nint _icon;

        private bool _ownsIcon;

        public WindowsDeploymentNotifications(
            nint window,
            Action<DeploymentCompletionNotice> activated,
            ILogger<WindowsDeploymentNotifications> logger)
        {
            _logger = logger;
            _window = window;
            _activated = activated;
            _callback = WindowCallback;
            if (!OperatingSystem.IsWindows() || window == 0)
            {
                return;
            }
            try
            {
                _available = SetWindowSubclass(window, _callback, 213, 0);
                if (Environment.ProcessPath is { } executable)
                {
                    _icon = ExtractIcon(0, executable, 0);
                    _ownsIcon = (long)_icon > 1;
                }
                if (!_ownsIcon)
                {
                    _icon = LoadIcon(0, 32516);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, InitializationFailureMessage);
                _available = false;
            }
        }

        public void Show(DeploymentCompletionNotice notice, string title, string message)
        {
            if (!_available || _disposed)
            {
                return;
            }
            try
            {
                if (_pending.Count >= 32)
                {
                    Remove(_pending.Keys.First());
                }
                do
                {
                    _nextId = _nextId % 65535 + 1;
                }
                while (_pending.ContainsKey(_nextId));

                var data = CreateData(_nextId);
                data.Flags = 1 | 2 | 4;
                data.Icon = _icon;
                data.Tip = ApplicationConstants.Name;
                if (!ShellNotifyIcon(0, ref data))
                {
                    return;
                }
                _pending.Add(_nextId, notice);
                data.Version = 4;
                if (!ShellNotifyIcon(4, ref data))
                {
                    Remove(_nextId);
                    return;
                }
                data.Flags = 0x10;
                data.InfoTitle = ShortText(title, 63);
                data.Info = ShortText(message, 255);
                data.InfoFlags = notice.Status == Enums.DeploymentStatus.Succeeded ? 1u : 2u;
                if (!ShellNotifyIcon(1, ref data))
                {
                    Remove(_nextId);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, DisplayFailureMessage);
                _available = false;
            }
        }

        public void Clear()
        {
            foreach (var id in _pending.Keys.ToArray())
            {
                Remove(id);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Clear();
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    RemoveWindowSubclass(_window, _callback, 213);
                    if (_ownsIcon)
                    {
                        DestroyIcon(_icon);
                        _ownsIcon = false;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, ResourceReleaseFailureMessage);
                }
            }
        }

        private nint WindowCallback(
            nint window,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            nuint referenceData)
        {
            try
            {
                if (message == CallbackMessage)
                {
                    var eventCode = (uint)((long)lParam & 0xFFFF);
                    var id = (uint)(((long)lParam >> 16) & 0xFFFF);
                    if (eventCode == 0x405 && _pending.TryGetValue(id, out var notice))
                    {
                        Remove(id);
                        _activated(notice);
                    }
                    else if (eventCode is 0x403 or 0x404)
                    {
                        Remove(id);
                    }
                    return 0;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, CallbackFailureMessage);
            }
            return DefSubclassProc(window, message, wParam, lParam);
        }

        private void Remove(uint id)
        {
            _pending.Remove(id);
            try
            {
                var data = CreateData(id);
                ShellNotifyIcon(2, ref data);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, RemovalFailureMessage);
            }
        }

        private NotifyIconData CreateData(uint id) => new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _window,
            Id = id,
            CallbackMessage = CallbackMessage,
            Tip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty,
        };

        private static string ShortText(string value, int length)
        {
            var text = new string(value.Where(character => !char.IsControl(character)).ToArray());
            if (text.Length <= length)
            {
                return text;
            }
            if (char.IsHighSurrogate(text[length - 1]))
            {
                length--;
            }
            return text[..length];
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint Size;
            public nint Window;
            public uint Id;
            public uint Flags;
            public uint CallbackMessage;
            public nint Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State;
            public uint StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Guid;
            public nint BalloonIcon;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint SubclassCallback(
            nint window,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            nuint referenceData);

        [DllImport(CommonControlsLibrary, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(nint window, SubclassCallback callback, nuint id, nuint data);

        [DllImport(CommonControlsLibrary)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(nint window, SubclassCallback callback, nuint id);

        [DllImport(CommonControlsLibrary)]
        private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

        [DllImport(ShellLibrary, EntryPoint = NotifyIconEntryPoint, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShellNotifyIcon(uint action, ref NotifyIconData data);

        [DllImport(UserLibrary, EntryPoint = LoadIconEntryPoint)]
        private static extern nint LoadIcon(nint instance, nint name);

        [DllImport(ShellLibrary, EntryPoint = ExtractIconEntryPoint, CharSet = CharSet.Unicode)]
        private static extern nint ExtractIcon(nint instance, string fileName, uint index);

        [DllImport(UserLibrary)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(nint icon);
    }
}
