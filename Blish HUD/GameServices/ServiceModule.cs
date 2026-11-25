using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Blish_HUD.GameServices {
    public abstract class ServiceModule<T> : IServiceModule
        where T : GameService {

        protected readonly T _service;

        protected ServiceModule(T service) {
            _service = service;
        }

        public virtual Task LoadAsync() => Task.CompletedTask;

        public virtual void Update(GameTime gameTime) { /* NOOP */ }

        public virtual void Unload() { /* NOOP */ }

    }
}
