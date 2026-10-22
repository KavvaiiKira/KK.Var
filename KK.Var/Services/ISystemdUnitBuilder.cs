using KK.Var.Models;

namespace KK.Var.Services
{
    public interface ISystemdUnitBuilder
    {
        string CreateUnit(KKProject project, string userName);
    }
}
