using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Screens;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.UI;

namespace LineLogisticsGame
{
    // Starts on the recipe screen, where the player picks what each resource is made of; Start opens the game screen.
    public class AppGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private SpriteFont font;

        private readonly Input input = new Input();
        private readonly TransportSystem transport = new TransportSystem();
        private RecipeScreen recipeScreen;
        private GameScreen gameScreen;
        private bool started;

        public AppGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            // hardware anti-aliasing on top of DrawShape's soft edges
            _graphics.PreparingDeviceSettings += (sender, e) =>
                e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 4;
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            Window.Title = "Line Logistics Game";
        }

        protected override void Initialize()
        {
            // borderless full screen at the desktop resolution (F11 switches to a window and back)
            SetFullScreen(true);

            recipeScreen = new RecipeScreen(transport.Recipes, () => started = true);
            gameScreen = new GameScreen(transport, Exit);

            base.Initialize();
        }

        private void SetFullScreen(bool fullScreen)
        {
            DisplayMode display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.HardwareModeSwitch = false;
            _graphics.IsFullScreen = fullScreen;
            _graphics.PreferredBackBufferWidth = fullScreen ? display.Width : 1280;
            _graphics.PreferredBackBufferHeight = fullScreen ? display.Height : 720;
            _graphics.PreferMultiSampling = true;
            _graphics.ApplyChanges();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            DrawShape.Initialize(GraphicsDevice);
            font = Content.Load<SpriteFont>("Font");
        }

        protected override void Update(GameTime gameTime)
        {
            input.Update();

            if (input.KeyPressed(Keys.F11)) SetFullScreen(!_graphics.IsFullScreen);

            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
                Exit();

            Viewport viewport = GraphicsDevice.Viewport;
            if (!started)
            {
                if (input.KeyPressed(Keys.Escape)) Exit();
                recipeScreen.Layout(viewport.Width, viewport.Height);
                if (IsActive) recipeScreen.Update(input);
            }
            else
            {
                gameScreen.Update(gameTime, input, viewport, IsActive);
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(30, 30, 36));

            if (!started) recipeScreen.Draw(_spriteBatch, font);
            else gameScreen.Draw(_spriteBatch, font, input, GraphicsDevice.Viewport);

            base.Draw(gameTime);
        }
    }
}
