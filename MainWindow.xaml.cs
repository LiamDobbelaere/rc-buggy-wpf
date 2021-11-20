using Microsoft.Extensions.DependencyInjection;
using SharpBrick.PoweredUp;
using SharpBrick.PoweredUp.Bluetooth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.Gaming.Input;

namespace RcBuggyWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Gamepad gamepad;
        Timer timer;
        TechnicMediumHub hub;
        PoweredUpHost host;
        TechnicLargeLinearMotor bMotor;
        TechnicLargeLinearMotor aMotor;
        sbyte lastAccelerationValue;
        sbyte lastTurnValue;
        List<Task> commandPool = new List<Task>();
        const int maxCommands = 5;
        Random rand;

        int turnDegreesLeft;
        int turnDegreesRight;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var serviceProvider = new ServiceCollection()
                .AddLogging()
                .AddPoweredUp()
                .AddWinRTBluetooth() // using WinRT Bluetooth on Windows (separate NuGet SharpBrick.PoweredUp.WinRT; others are available)
                .BuildServiceProvider();

            host = serviceProvider.GetService<PoweredUpHost>();
            rand = new Random();

            Log("Looking for hubs...");
            hub = await host.DiscoverAsync<TechnicMediumHub>();
            Log("Connecting to hub...");
            await hub.ConnectAsync();
            Log("Hub connected!");
            _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Blue);
            aMotor = hub.A.GetDevice<TechnicLargeLinearMotor>();
            bMotor = hub.B.GetDevice<TechnicLargeLinearMotor>();
            await bMotor.GotoRealZeroAsync();
            await bMotor.SetupNotificationAsync(bMotor.ModeIndexAbsolutePosition, true);

            _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Red);

            for (int i = 0; i < 4; i++)
            {
                await bMotor.StartPowerAsync(100);
                _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Orange);
                await Task.Delay(800);
                if (bMotor.AbsolutePosition > turnDegreesRight)
                {
                    turnDegreesRight = bMotor.AbsolutePosition;
                }

                await bMotor.StartPowerAsync(-100);
                _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Red);
                await Task.Delay(800);
                if (bMotor.AbsolutePosition < turnDegreesLeft)
                {
                    turnDegreesLeft = bMotor.AbsolutePosition;
                }
            }
            _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Yellow);
            await bMotor.GotoRealZeroAsync();

            turnDegreesLeft = turnDegreesLeft + Math.Sign(turnDegreesLeft) * 5;
            turnDegreesRight = turnDegreesRight + Math.Sign(turnDegreesRight) * 5;

            Log("TDR: " + turnDegreesRight.ToString() + ", TDL: " + turnDegreesLeft.ToString());
            await Task.Delay(5000);
            if (hub.BatteryVoltageInPercent > 75)
            {
                _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Green);
            }
            else if (hub.BatteryVoltageInPercent > 50)
            {
                _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Yellow);
            }
            else if (hub.BatteryVoltageInPercent > 25)
            {
                _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Orange);
            }
            else
            {
                _ = hub.RgbLight.SetRgbColorNoAsync(PoweredUpColor.Red);
            }

            timer = new Timer(TimerTick, null, 0, 16);
        }

        private void Log(string text)
        {
            richTextBox.Dispatcher.Invoke(() =>
            {
                TextRange tr = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd);
                tr.Text = text;
            });
        }

        private void TimerTick(Object stateInfo)
        {
            if (gamepad == null && Gamepad.Gamepads.Count > 0)
            {
                gamepad = Gamepad.Gamepads[0];
                Log("");
            }

            if (gamepad == null)
            {
                Log("No gamepad found");
                return;
            }

            GamepadReading currentReading = gamepad.GetCurrentReading();

            // Log(((sbyte)(currentReading.LeftThumbstickX * 100)).ToString());
            // await hub.RgbLight.SetRgbColorsAsync(0x00, 0xff, (byte) (currentReading.LeftThumbstickX * 255));

            sbyte currentAccelerationValue = (sbyte)((currentReading.RightTrigger * 100) + (currentReading.LeftTrigger * -100));
            sbyte currentTurnValue = (sbyte)(currentReading.LeftThumbstickX * 100);
            if (Math.Abs(currentTurnValue) < 5)
            {
                currentTurnValue = 0;
            }

            if (Math.Abs(currentAccelerationValue) < 25)
            {
                currentAccelerationValue = 0;
            }

            if (commandPool.Count < maxCommands)
            {
                if (Math.Abs(currentAccelerationValue - lastAccelerationValue) > 15)
                {
                    commandPool.Add(aMotor.StartPowerAsync((sbyte) -currentAccelerationValue));
                    lastAccelerationValue = currentAccelerationValue;
                }

                if (Math.Abs(currentTurnValue - lastTurnValue) > 15 || (currentTurnValue == 0 && currentTurnValue != lastTurnValue))
                {
                    if (currentTurnValue == 0 && Math.Abs(bMotor.AbsolutePosition) > 10)
                    {
                        commandPool.Add(bMotor.GotoRealZeroAsync());
                    }
                    else 
                    {
                        if (currentTurnValue > 0)
                        {
                            commandPool.Add(
                                bMotor.GotoPositionAsync((int) (turnDegreesRight * (Math.Abs(currentTurnValue) / 100.0)), 100, 100, SpecialSpeed.Brake)
                            );
                        }
                        else
                        {
                            commandPool.Add(
                                bMotor.GotoPositionAsync((int)(turnDegreesLeft * (Math.Abs(currentTurnValue) / 100.0)), 100, 100, SpecialSpeed.Brake)
                            );
                        }

                    }
                    lastTurnValue = currentTurnValue;
                }
            }

            commandPool.RemoveAll(t => t.IsCompleted);
        }
    }
}
