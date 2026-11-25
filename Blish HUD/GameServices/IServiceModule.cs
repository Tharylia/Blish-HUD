using System.Threading.Tasks;

namespace Blish_HUD.GameServices {
    internal interface IServiceModule : IUpdatable {

        Task LoadAsync();

        void Unload();

    }
}
