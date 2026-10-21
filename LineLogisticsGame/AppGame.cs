using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Persistence;
using LineLogisticsGame.Screens;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.UI;

namespace LineLogisticsGame
{
    // Starts on the splash screen, then the menu. New game opens the recipe screen, where the player picks what each resource
    // is made of; Start opens the game screen. Continue loads the most recent save, Load game lets the player pick one.
    // While playing, the Menu button (or Esc) opens the in-game menu: Back, Save, Main menu, Quit; Back, Esc or the Menu button closes it.
    // The Stats button next to it opens the statistics over the running game; Close, Esc or the Stats button closes them.
    // A game is saved to its own file when it starts, on F5 or Save, when leaving it for the main menu and when the window closes.
    public class AppGame : Game
    {
        private enum State { Splash, Menu, Recipes, LoadGame, Playing }

        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private SpriteFont font;
        private SpriteFont titleFont;
        private SpriteFont smallTitleFont;

        private readonly Input input = new Input();
        private State state = State.Splash;
        private SplashScreen splashScreen;
        private MenuScreen menuScreen;
        private LoadGameScreen loadGameScreen;
        private MenuScreen gameMenu;        // in-game menu, over the paused game
        private bool gameMenuOpen;
        private StatsScreen statsScreen;    // statistics, over the running game
        private bool statsOpen;

        // the game being set up or played
        private TransportSystem transport;
        private RecipeScreen recipeScreen;
        private GameScreen gameScreen;
        private string savePath;
        private bool hasSaves;

        // a short note shown at the bottom of the screen ("Game saved"), and for how much longer
        private string message;
        private float messageTime;

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

            splashScreen = new SplashScreen(OpenMenu);
            menuScreen = new MenuScreen("Line Logistics",
                new MenuScreen.Option { Label = "Continue", IsEnabled = () => hasSaves, Choose = Continue },
                new MenuScreen.Option { Label = "New game", Choose = NewGame },
                new MenuScreen.Option { Label = "Load game", IsEnabled = () => hasSaves, Choose = OpenLoadGame },
                new MenuScreen.Option { Label = "Exit", Choose = Exit });
            loadGameScreen = new LoadGameScreen(LoadGame, OpenMenu);
            gameMenu = new MenuScreen("Menu",
                new MenuScreen.Option { Label = "Back", Choose = () => gameMenuOpen = false },
                new MenuScreen.Option { Label = "Save", Choose = () => { Save(true); gameMenuOpen = false; } },
                new MenuScreen.Option { Label = "Main menu", Choose = LeaveGame },
                new MenuScreen.Option { Label = "Quit", Choose = Exit });   // OnExiting saves

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
            titleFont = Content.Load<SpriteFont>("TitleFont");
            smallTitleFont = Content.Load<SpriteFont>("SmallTitleFont");
        }

        #region Screens

        private void OpenMenu()
        {
            hasSaves = SaveGame.All().Count > 0;
            state = State.Menu;
        }

        private void NewGame()
        {
            transport = new TransportSystem();
            recipeScreen = new RecipeScreen(transport.Recipes, StartNewGame);
            state = State.Recipes;
        }

        private void StartNewGame()
        {
            gameScreen = new GameScreen(transport, () => gameMenuOpen = true, () => statsOpen = true);
            statsScreen = new StatsScreen(transport, gameScreen.Drawing, () => statsOpen = false);
            savePath = SaveGame.NewPath();
            state = State.Playing;
            gameMenuOpen = false;
            statsOpen = false;
            Save(false);
        }

        private void Continue()
        {
            var latest = SaveGame.All().FirstOrDefault();
            if (latest.Path != null) LoadGame(latest.Path);
        }

        private void OpenLoadGame()
        {
            loadGameScreen.Refresh();
            state = State.LoadGame;
        }

        private void LoadGame(string path)
        {
            try
            {
                SaveGame save = SaveGame.Read(path);
                transport = new TransportSystem();
                gameScreen = new GameScreen(transport, () => gameMenuOpen = true, () => statsOpen = true);
                statsScreen = new StatsScreen(transport, gameScreen.Drawing, () => statsOpen = false);
                save.Apply(transport, gameScreen);
                savePath = path;
                state = State.Playing;
                gameMenuOpen = false;
                statsOpen = false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Text.Json.JsonException
                                      || e is InvalidDataException || e is ArgumentOutOfRangeException || e is IndexOutOfRangeException)
            {
                ShowMessage("Could not load the game");
                OpenMenu();
            }
        }

        private void LeaveGame()
        {
            Save(false);
            gameMenuOpen = false;
            OpenMenu();
        }

        private void Save(bool announce)
        {
            if (state != State.Playing) return;
            try
            {
                SaveGame.Write(savePath, transport, gameScreen);
                if (announce) ShowMessage("Game saved");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ShowMessage("Could not save the game");
            }
        }

        private void ShowMessage(string text)
        {
            message = text;
            messageTime = 2f;
        }

        protected override void OnExiting(object sender, ExitingEventArgs args)
        {
            Save(false);
            base.OnExiting(sender, args);
        }

        #endregion

        protected override void Update(GameTime gameTime)
        {
            input.Update();
            input.Camera = null;    // only the game screen works in world coordinates; it sets this every frame
            messageTime -= (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (input.KeyPressed(Keys.F11)) SetFullScreen(!_graphics.IsFullScreen);

            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
                Exit();

            Viewport viewport = GraphicsDevice.Viewport;
            switch (state)
            {
                case State.Splash:
                    splashScreen.Update(gameTime);
                    break;

                case State.Menu:
                    menuScreen.Layout(viewport.Width, viewport.Height);
                    if (IsActive)
                    {
                        if (input.KeyPressed(Keys.Escape)) Exit();
                        else menuScreen.Update(input);
                    }
                    break;

                case State.Recipes:
                    recipeScreen.Layout(viewport.Width, viewport.Height);
                    if (IsActive)
                    {
                        if (input.KeyPressed(Keys.Escape)) OpenMenu();
                        else recipeScreen.Update(input);
                    }
                    break;

                case State.LoadGame:
                    loadGameScreen.Layout(viewport.Width, viewport.Height);
                    if (IsActive) loadGameScreen.Update(input);
                    break;

                case State.Playing:
                    if (gameMenuOpen)
                    {
                        // the game is paused behind it; Back, Esc or the Menu button closes it
                        gameMenu.Layout(viewport.Width, viewport.Height);
                        if (IsActive)
                        {
                            if (input.KeyPressed(Keys.Escape) || (input.LeftPressed && gameScreen.MenuButton.Contains(input.ScreenMousePosition)))
                                gameMenuOpen = false;
                            else gameMenu.Update(input);
                        }
                    }
                    else if (statsOpen)
                    {
                        // the game keeps running behind it, without input; Close, Esc or the Stats button closes it
                        gameScreen.Simulate(gameTime);
                        statsScreen.Layout(viewport.Width, viewport.Height);
                        if (IsActive)
                        {
                            if (input.LeftPressed && gameScreen.StatsButton.Contains(input.ScreenMousePosition))
                                statsOpen = false;
                            else statsScreen.Update(input);
                        }
                    }
                    else
                    {
                        if (IsActive && input.KeyPressed(Keys.F5)) Save(true);
                        gameScreen.Update(gameTime, input, viewport, IsActive);
                    }
                    break;
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(30, 30, 36));
            Viewport viewport = GraphicsDevice.Viewport;

            switch (state)
            {
                case State.Splash: splashScreen.Draw(_spriteBatch, smallTitleFont, titleFont, viewport); break;
                case State.Menu: menuScreen.Draw(_spriteBatch, font, titleFont); break;
                case State.Recipes: recipeScreen.Draw(_spriteBatch, font); break;
                case State.LoadGame: loadGameScreen.Draw(_spriteBatch, font, titleFont, input); break;
                case State.Playing:
                    gameScreen.Draw(_spriteBatch, font, input, viewport);
                    if (gameMenuOpen)
                    {
                        DrawShape.DrawFilledRectangle(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.6f);
                        gameMenu.Draw(_spriteBatch, font, titleFont);
                    }
                    else if (statsOpen)
                    {
                        DrawShape.DrawFilledRectangle(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.5f);
                        statsScreen.Draw(_spriteBatch, font);
                    }
                    break;
            }

            if (messageTime > 0 && message != null)
            {
                Vector2 size = font.MeasureString(message);
                var box = new Rectangle((int)((viewport.Width - size.X) / 2f) - 12, viewport.Height - 70, (int)size.X + 24, (int)size.Y + 12);
                DrawShape.DrawFilledRectangle(box, Color.Black * 0.7f);
                DrawShape.Flush();
                _spriteBatch.Begin();
                _spriteBatch.DrawString(font, message, new Vector2(box.X + 12, box.Y + 6), Color.White * Math.Min(1f, messageTime * 2f));
                _spriteBatch.End();
            }

            DrawShape.Flush();
            base.Draw(gameTime);
        }
    }
}
