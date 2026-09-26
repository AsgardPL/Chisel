using Engine.Console;
using Gum.Forms.Controls;
using Microsoft.Xna.Framework;
using MonoGameGum;
using MonoGameGum.Forms;
using MonoGameGum.GueDeriving;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.UI
{
    public class GameConsoleWindow
    {
        private static GameConsoleWindow Instance => MainEngine.Instance.ConsoleWindow;
        public static readonly CommandBinding cClear = new("clear", _ => Instance.Clear());
        public CGWindow mainWindow { get; private set; }
        public TextBox consoleInput { get; private set; }
        public bool Opened { get; private set; }

        private ScrollViewer scroller;
        private Label outputText;
        private StringBuilder outputString = new StringBuilder();

        private List<string> history = [];
        private int historyIndex = -1;

        public void Initialize()
        {
            mainWindow = new CGWindow("Console");
            mainWindow.Width = 600;
            mainWindow.Height = 400;
            mainWindow.MinWidth = 320;
            mainWindow.MinHeight = 320;

            mainWindow.Anchor(Gum.Wireframe.Anchor.TopRight);

            consoleInput = new CGTextBoxRuntime().FormsControl;

            var background = new CGDarkPanel(35);
            background.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            background.HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;

            mainWindow.InnerPanel.AddChild(background);

            background.Anchor(Gum.Wireframe.Anchor.Top);
            background.Width = 0;
            background.Height = -32;

            outputText = new Label();
            (outputText.Visual as TextRuntime).BitmapFont = Styling.Default24;

            scroller = new CGScrollViewer().FormsControl;
            scroller.InnerPanel.AddChild(outputText);

            background.AddChild(scroller);
            scroller.Dock(Gum.Wireframe.Dock.Fill);
            scroller.InnerPanel.StackSpacing = 2;

            mainWindow.InnerPanel.AddChild(consoleInput);
            consoleInput.Anchor(Gum.Wireframe.Anchor.Bottom);
            consoleInput.Dock(Gum.Wireframe.Dock.Bottom);
            consoleInput.KeyDown += ConsoleInput_KeyDown;
            consoleInput.PreviewTextInput += ConsoleInput_PreviewTextInput;

            Opened = true;
            mainWindow.Open();

            // GC can eat commands like this if they're unused.
            GC.KeepAlive(cClear);
        }

        private void ConsoleInput_PreviewTextInput(object arg1, Gum.Forms.Controls.TextCompositionEventArgs arg2)
        {
            arg2.Handled = (arg2.Text == "`");
        }

        private void ConsoleInput_KeyDown(object arg1, Gum.Forms.Controls.KeyEventArgs arg2)
        {
            switch(arg2.Key)
            {
                case Microsoft.Xna.Framework.Input.Keys.Enter:

                    string cmd = consoleInput.Text;
                    consoleInput.Text = "";
                    
                    if (!string.IsNullOrWhiteSpace(cmd))
                        history.Insert(0, cmd);
                    
                    historyIndex = -1;
                    MainEngine.Instance.Console.Execute(cmd);

                    break;

                case Microsoft.Xna.Framework.Input.Keys.Up:
                    SetInputFromHistory(historyIndex + 1);
                    break;

                case Microsoft.Xna.Framework.Input.Keys.Down:
                    SetInputFromHistory(historyIndex - 1);
                    break;
            }
        }

        private void SetInputFromHistory(int index)
        {
            if (consoleInput.Text != string.Empty && historyIndex == -1)
                return;

            if (index >= history.Count)
                return;

            if (history.Count == 0)
                return;

            if(index == -1)
            {
                historyIndex = -1;
                consoleInput.Text = "";
                return;
            }

            if (index < 0)
                return;

            historyIndex = index;
            consoleInput.Text = history[index];
            consoleInput.CaretIndex = consoleInput.Text!.Length;
        }

        public void Append(string line)
        {
            outputString.AppendLine(line);
            outputText.Text = outputString.ToString();

            if (scroller.VerticalScrollBarMaximum - scroller.VerticalScrollBarValue <= 200)
            {
                scroller.ScrollToBottom();
            }
        }

        public void Close()
        {
            Opened = false;
            consoleInput.IsFocused = false;
            mainWindow.Close();
        }
        public void Open()
        {
            Opened = true;
            consoleInput.IsFocused = true;
            mainWindow.Open();
        }
        public void Clear()
        {
            scroller.InnerPanel.Children?.Clear();
        }
        public void EnsureConsoleOnTop()
        {
            MainEngine.Gum.Root.Children.Move(MainEngine.Gum.Root.Children.IndexOf(mainWindow), MainEngine.Gum.Root.Children.Count - 1);
        }
    }
}
