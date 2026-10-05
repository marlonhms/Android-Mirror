using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace AuraScrcpy
{
    public class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;

        private static Process currentScrcpyProcess = null;
        private static bool isScrcpyHidden = false;

        private static Button btnToggleVisibility;
        private static TextBlock txtToggleVisibilityIcon;
        private static TextBlock txtToggleVisibilityText;

        private static Window mainWindow;
        private static string baseDir;
        private static string adbPath;
        private static string scrcpyPath;
        private static string configFile;
        private static string lastIpFile;

        // UI Controls - Connection
        private static RadioButton rbUsb;
        private static RadioButton rbWifi;
        private static StackPanel panelUsb;
        private static StackPanel panelWifi;
        private static TextBlock txtUsbStatus;
        private static Button btnRefreshUsb;
        private static Button btnEnableTcpip;
        private static TextBox txtIp;
        private static Button btnAutoDetectWifi;
        private static Button btnTestPing;
        private static Button btnAdbConnect;
        private static Button btnPairWifi;
        private static Button btnFixPort5555;
        private static TextBlock txtWifiStatus;
        private static TextBlock txtPingResult;

        // UI Controls - Presets
        private static Button btnPresetUltra;
        private static Button btnPresetGaming;
        private static Button btnPresetBalanced;
        private static Button btnPreset2K;
        private static Button btnPresetEco;
        private static Button btnPresetWebcamPro;
        private static TextBlock txtSelectedPreset;

        // UI Controls - Toggles
        private static CheckBox chkScreenOff;
        private static CheckBox chkStayAwake;
        private static CheckBox chkAudio;
        private static CheckBox chkAlwaysOnTop;
        private static CheckBox chkBorderless;
        private static CheckBox chkFullscreen;
        private static CheckBox chkShowTouches;
        private static CheckBox chkRecord;
        private static CheckBox chkCamera;
        private static CheckBox chkOtg;
        private static CheckBox chkCloseOnLaunch;
        private static CheckBox chkInvisibleMode;
        private static CheckBox chkNoVirtualKeyboard;

        // UI Controls - Advanced Tuning
        private static ComboBox cmbResolution;
        private static ComboBox cmbFps;
        private static ComboBox cmbBitrate;
        private static ComboBox cmbCodec;
        private static ComboBox cmbBuffer;
        private static ComboBox cmbCameraFacing;
        private static ComboBox cmbCameraOrientation;

        // UI Controls - Actions & Status
        private static Button btnLaunch;
        private static Button btnQuickUsb;
        private static Button btnStop;
        private static TextBlock txtStatus;
        private static TextBox txtCmdPreview;

        // State & Timers
        private static DispatcherTimer autoDetectTimer;
        private static bool isApplyingPreset = false;
        private static string currentPresetName = "Ultra";
        private static string lastDetectedUsbDevice = null;
        private static string lastDetectedUsbState = null;
        private static bool isCheckingUsb = false;
        private static bool isCheckingWifi = false;
        private static bool isAutoDetecting = false;
        private static bool isTestingPing = false;

        public static void SafeInvoke(Action act)
        {
            try
            {
                if (mainWindow != null && mainWindow.Dispatcher != null && !mainWindow.Dispatcher.HasShutdownStarted)
                {
                    if (mainWindow.Dispatcher.CheckAccess())
                    {
                        act();
                    }
                    else
                    {
                        mainWindow.Dispatcher.Invoke(act);
                    }
                }
            }
            catch { }
        }

        [STAThread]
        public static void Main()
        {
            try
            {
                baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                adbPath = Path.Combine(baseDir, "adb.exe");
                scrcpyPath = Path.Combine(baseDir, "scrcpy.exe");
                configFile = Path.Combine(baseDir, "aura_settings.ini");
                lastIpFile = Path.Combine(baseDir, "last_ip.txt");

                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) {
                    try {
                        Exception ex = e.ExceptionObject as Exception;
                        string msg = ex != null ? ex.ToString() : "Erro desconhecido";
                        File.AppendAllText(Path.Combine(baseDir, "aura_crash.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [AppDomain]: " + msg + Environment.NewLine);
                    } catch { }
                };

                Application app = new Application();

                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                    try {
                        File.AppendAllText(Path.Combine(baseDir, "aura_crash.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [Dispatcher]: " + e.Exception.ToString() + Environment.NewLine);
                        e.Handled = true;
                    } catch { }
                };

                mainWindow = BuildWindow();
                InitLogic();
                LoadSettings();
                UpdateCommandPreview();
                CheckUsbStatusAsync(false);

                // Auto-detect hotplug every 3.5 seconds
                autoDetectTimer = new DispatcherTimer();
                autoDetectTimer.Interval = TimeSpan.FromMilliseconds(3500);
                autoDetectTimer.Tick += delegate {
                    CheckUsbStatusAsync(true);
                };
                autoDetectTimer.Start();

                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro fatal ao inicializar Aura SCRCPY:\n" + ex.Message, "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Window BuildWindow()
        {
            string xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='AURA SCRCPY - Início Inteligente'
        Width='720' Height='830'
        WindowStyle='None'
        AllowsTransparency='True'
        Background='Transparent'
        ResizeMode='NoResize'
        WindowStartupLocation='CenterScreen'
        FontFamily='Segoe UI, Segoe UI Semibold, Roboto, Arial'>

    <Window.Resources>
        <Style TargetType='TextBlock'>
            <Setter Property='Foreground' Value='#E2E8F0'/>
        </Style>

        <Style TargetType='CheckBox'>
            <Setter Property='Foreground' Value='#CBD5E1'/>
            <Setter Property='FontSize' Value='12.5'/>
            <Setter Property='Margin' Value='0,3.5,0,3.5'/>
            <Setter Property='Cursor' Value='Hand'/>
        </Style>

        <Style TargetType='RadioButton'>
            <Setter Property='Foreground' Value='#F1F5F9'/>
            <Setter Property='FontSize' Value='13.5'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='Cursor' Value='Hand'/>
        </Style>

        <ControlTemplate x:Key='ComboBoxToggleButton' TargetType='ToggleButton'>
            <Border Name='Border' Background='#1E293B' BorderBrush='#334155' BorderThickness='1' CornerRadius='6'>
                <Border Name='ButtonBorder' Background='Transparent' HorizontalAlignment='Right' Width='24'>
                    <Path Name='Arrow' Data='M 0 0 L 4 4 L 8 0 Z' Fill='#94A3B8' HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'>
                    <Setter TargetName='Border' Property='BorderBrush' Value='#38BDF8'/>
                    <Setter TargetName='Arrow' Property='Fill' Value='#38BDF8'/>
                </Trigger>
                <Trigger Property='IsChecked' Value='True'>
                    <Setter TargetName='Border' Property='BorderBrush' Value='#10B981'/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>

        <Style TargetType='ComboBox'>
            <Setter Property='Foreground' Value='#F8FAFC'/>
            <Setter Property='FontSize' Value='12'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='SnapsToDevicePixels' Value='True'/>
            <Setter Property='OverridesDefaultStyle' Value='True'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ComboBox'>
                        <Grid>
                            <ToggleButton Name='ToggleButton' Template='{StaticResource ComboBoxToggleButton}' Focusable='false'
                                          IsChecked='{Binding Path=IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'
                                          ClickMode='Press'/>
                            <ContentPresenter Name='ContentSite' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}'
                                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                                              ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}'
                                              Margin='10,6,28,6' VerticalAlignment='Center' HorizontalAlignment='Left'/>
                            <Popup Name='Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}'
                                   AllowsTransparency='True' Focusable='False' PopupAnimation='Slide'>
                                <Grid Name='DropDown' SnapsToDevicePixels='True' MinWidth='{TemplateBinding ActualWidth}' MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                                    <Border Name='DropDownBorder' Background='#1E293B' BorderBrush='#334155' BorderThickness='1' CornerRadius='6' Margin='0,2,0,4'>
                                        <ScrollViewer Margin='2' SnapsToDevicePixels='True'>
                                            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Contained'/>
                                        </ScrollViewer>
                                    </Border>
                                </Grid>
                            </Popup>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style TargetType='ComboBoxItem'>
            <Setter Property='SnapsToDevicePixels' Value='True'/>
            <Setter Property='OverridesDefaultStyle' Value='True'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ComboBoxItem'>
                        <Border Name='Border' Padding='10,7' Background='Transparent' CornerRadius='4' Margin='2,1'>
                            <ContentPresenter Content='{TemplateBinding Content}' TextBlock.Foreground='#F8FAFC' TextBlock.FontSize='12'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsHighlighted' Value='True'>
                                <Setter TargetName='Border' Property='Background' Value='#2563EB'/>
                            </Trigger>
                            <Trigger Property='IsSelected' Value='True'>
                                <Setter TargetName='Border' Property='Background' Value='#1D4ED8'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style TargetType='TextBox'>
            <Setter Property='Background' Value='#0F172A'/>
            <Setter Property='Foreground' Value='#F8FAFC'/>
            <Setter Property='BorderBrush' Value='#334155'/>
            <Setter Property='BorderThickness' Value='1'/>
            <Setter Property='Padding' Value='8,6'/>
            <Setter Property='FontSize' Value='13'/>
        </Style>
    </Window.Resources>

    <Border Background='#0B0F19' CornerRadius='14' BorderBrush='#1E293B' BorderThickness='1'>
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height='54'/> <!-- Titlebar -->
                <RowDefinition Height='*'/>  <!-- Content -->
                <RowDefinition Height='38'/> <!-- Statusbar -->
            </Grid.RowDefinitions>

            <!-- 1. Custom Title Bar -->
            <Border Grid.Row='0' Background='#111827' CornerRadius='14,14,0,0' BorderBrush='#1E293B' BorderThickness='0,0,0,1' Name='TitleBarBorder' Cursor='SizeAll'>
                <Grid Margin='14,0,14,0'>
                    <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
                        <Border Width='28' Height='28' CornerRadius='6' Background='#047857' Margin='0,0,10,0'>
                            <TextBlock Text='⚡' FontSize='16' HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <StackPanel VerticalAlignment='Center'>
                            <StackPanel Orientation='Horizontal'>
                                <TextBlock Text='AURA' FontWeight='Bold' FontSize='15' Foreground='#10B981'/>
                                <TextBlock Text=' SCRCPY' FontWeight='Bold' FontSize='15' Foreground='#38BDF8'/>
                                <Border Background='#1E293B' CornerRadius='4' Margin='8,0,0,0' Padding='6,1'>
                                    <TextBlock Text='v4.1 PRO' FontSize='10' FontWeight='Bold' Foreground='#94A3B8'/>
                                </Border>
                            </StackPanel>
                            <TextBlock Text='Painel Otimizado de Espelhamento e Produtividade' FontSize='11' Foreground='#64748B'/>
                        </StackPanel>
                    </StackPanel>

                    <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                        <Button Name='BtnMinimize' Content='─' Width='32' Height='28' Background='Transparent' Foreground='#94A3B8' BorderThickness='0' FontSize='14' Cursor='Hand'/>
                        <Button Name='BtnClose' Content='✕' Width='32' Height='28' Background='Transparent' Foreground='#94A3B8' BorderThickness='0' FontSize='14' FontWeight='Bold' Cursor='Hand'/>
                    </StackPanel>
                </Grid>
            </Border>

            <!-- 2. Main Scrollable Content -->
            <ScrollViewer Grid.Row='1' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' Margin='12,6,12,4'>
                <StackPanel Margin='4'>

                    <!-- Section: Connection Type (USB vs Wi-Fi) -->
                    <Border Background='#131C2E' CornerRadius='10' BorderBrush='#1E293B' BorderThickness='1' Padding='14' Margin='0,0,0,9'>
                        <StackPanel>
                            <Grid Margin='0,0,0,10'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='*'/>
                                </Grid.ColumnDefinitions>
                                <RadioButton Name='RbUsb' Content='⚡ Conexão USB (Cabo)' IsChecked='True' Grid.Column='0'/>
                                <RadioButton Name='RbWifi' Content='📶 Conexão Wi-Fi (Sem Fio)' Grid.Column='1'/>
                            </Grid>

                            <!-- USB Panel -->
                            <StackPanel Name='PanelUsb' Visibility='Visible'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='*'/>
                                        <ColumnDefinition Width='Auto'/>
                                        <ColumnDefinition Width='Auto'/>
                                    </Grid.ColumnDefinitions>
                                    <StackPanel Grid.Column='0' VerticalAlignment='Center'>
                                        <TextBlock Text='Status do Dispositivo USB:' FontSize='11' Foreground='#64748B'/>
                                        <TextBlock Name='TxtUsbStatus' Text='Detectando via ADB...' FontSize='13' FontWeight='SemiBold' Foreground='#FACC15' TextTrimming='CharacterEllipsis'/>
                                    </StackPanel>
                                    <Button Name='BtnRefreshUsb' Content='🔄 Atualizar' Grid.Column='1' Margin='6,0,0,0' Padding='10,5' Background='#1E293B' Foreground='#E2E8F0' BorderBrush='#334155' BorderThickness='1' Cursor='Hand'/>
                                    <Button Name='BtnEnableTcpip' Content='📲 Ativar Wi-Fi (TCP/IP)' Grid.Column='2' Margin='6,0,0,0' Padding='10,5' Background='#065F46' Foreground='#A7F3D0' BorderBrush='#047857' BorderThickness='1' Cursor='Hand'/>
                                </Grid>
                            </StackPanel>

                            <!-- Wi-Fi Panel -->
                            <StackPanel Name='PanelWifi' Visibility='Collapsed'>
                                <Grid Margin='0,2,0,4'>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='*'/>
                                        <ColumnDefinition Width='Auto'/>
                                        <ColumnDefinition Width='Auto'/>
                                        <ColumnDefinition Width='Auto'/>
                                    </Grid.ColumnDefinitions>
                                    <StackPanel Grid.Column='0'>
                                        <TextBlock Text='Endereço IP e Porta do Celular (ex: 192.168.1.100:37279 ou :5555):' FontSize='11' Foreground='#64748B' Margin='0,0,0,3'/>
                                        <TextBox Name='TxtIp' Text='192.168.1.100:5555'/>
                                    </StackPanel>
                                    <Button Name='BtnAutoDetectWifi' Content='🔍 Auto-Detectar' Grid.Column='1' VerticalAlignment='Bottom' Margin='6,0,0,0' Padding='9,7' Background='#1E293B' Foreground='#FCD34D' BorderBrush='#D97706' BorderThickness='1' FontWeight='SemiBold' Cursor='Hand' ToolTip='Busca celulares com Depuração Wi-Fi via mDNS automaticamente na rede.'/>
                                    <Button Name='BtnTestPing' Content='⚡ Testar Ping' Grid.Column='2' VerticalAlignment='Bottom' Margin='6,0,0,0' Padding='9,7' Background='#1E293B' Foreground='#38BDF8' BorderBrush='#0284C7' BorderThickness='1' FontWeight='SemiBold' Cursor='Hand' ToolTip='Mede o atraso de rede (latência) com o aparelho.'/>
                                    <Button Name='BtnAdbConnect' Content='🔗 Conectar ADB' Grid.Column='3' VerticalAlignment='Bottom' Margin='6,0,0,0' Padding='9,7' Background='#1E293B' Foreground='#A7F3D0' BorderBrush='#059669' BorderThickness='1' FontWeight='SemiBold' Cursor='Hand' ToolTip='Conecta o ADB ao endereço IP e porta especificados.'/>
                                </Grid>

                                <Grid Margin='0,5,0,2'>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='Auto'/>
                                        <ColumnDefinition Width='Auto'/>
                                        <ColumnDefinition Width='*'/>
                                    </Grid.ColumnDefinitions>
                                    <Button Name='BtnPairWifi' Content='🔑 Parear (Android 11+)' Grid.Column='0' Padding='9,4' Background='#1E293B' Foreground='#C084FC' BorderBrush='#9333EA' BorderThickness='1' Cursor='Hand' ToolTip='Abre assistente para parear via código de 6 dígitos da Depuração por Wi-Fi.'/>
                                    <Button Name='BtnFixPort5555' Content='📲 Fixar Porta 5555' Grid.Column='1' Margin='6,0,0,0' Padding='9,4' Background='#1E293B' Foreground='#6EE7B7' BorderBrush='#059669' BorderThickness='1' Cursor='Hand' ToolTip='Define porta 5555 permanente para não depender de portas dinâmicas.'/>
                                    <TextBlock Name='TxtWifiStatus' Text='📶 Depuração Wi-Fi: portas dinâmicas mudam ao religar.' Grid.Column='2' VerticalAlignment='Center' HorizontalAlignment='Right' FontSize='10.5' Foreground='#64748B' TextTrimming='CharacterEllipsis' Margin='6,0,0,0'/>
                                </Grid>

                                <TextBlock Name='TxtPingResult' Text='Ping não testado. Latência abaixo de 20ms é ideal para jogos sem atraso.' FontSize='11' Foreground='#94A3B8' Margin='2,3,0,0'/>
                            </StackPanel>
                        </StackPanel>
                    </Border>

                    <!-- Section: Presets de Otimização -->
                    <Border Background='#131C2E' CornerRadius='10' BorderBrush='#1E293B' BorderThickness='1' Padding='14' Margin='0,0,0,9'>
                        <StackPanel>
                            <Grid Margin='0,0,0,8'>
                                <TextBlock Text='PERFIS DE OTIMIZAÇÃO INTELIGENTE' FontSize='12' FontWeight='Bold' Foreground='#10B981'/>
                                <TextBlock Name='TxtSelectedPreset' Text='Perfil: Ultra Competitivo' HorizontalAlignment='Right' FontSize='11' Foreground='#38BDF8' FontWeight='SemiBold'/>
                            </Grid>

                            <UniformGrid Columns='3' Margin='0,0,0,6'>
                                <Button Name='BtnPresetUltra' Margin='3' Padding='8,8' Background='#064E3B' BorderBrush='#10B981' BorderThickness='1' Cursor='Hand'>
                                    <StackPanel>
                                        <TextBlock Text='⚡ Ultra Competitivo' FontWeight='Bold' FontSize='12' Foreground='#34D399'/>
                                        <TextBlock Text='90 FPS | 24M | 0ms Delay' FontSize='10' Foreground='#94A3B8'/>
                                    </StackPanel>
                                </Button>

                                <Button Name='BtnPresetGaming' Margin='3' Padding='8,8' Background='#1E293B' BorderBrush='#334155' BorderThickness='1' Cursor='Hand'>
                                    <StackPanel>
                                        <TextBlock Text='🎮 Gaming Fluido' FontWeight='Bold' FontSize='12' Foreground='#38BDF8'/>
                                        <TextBlock Text='90 FPS | 16M | H.265 | Opus' FontSize='10' Foreground='#94A3B8'/>
                                    </StackPanel>
                                </Button>

                                <Button Name='BtnPresetBalanced' Margin='3' Padding='8,8' Background='#1E293B' BorderBrush='#334155' BorderThickness='1' Cursor='Hand'>
                                    <StackPanel>
                                        <TextBlock Text='⚖️ Equilibrado Diário' FontWeight='Bold' FontSize='12' Foreground='#FCD34D'/>
                                        <TextBlock Text='60 FPS | 10M | 1080p | Opus' FontSize='10' Foreground='#94A3B8'/>
                                    </StackPanel>
                                </Button>
                            </UniformGrid>

                            <UniformGrid Columns='2' Margin='0,0,0,0'>
                                <Button Name='BtnPreset2K' Margin='3' Padding='8,8' Background='#1E293B' BorderBrush='#334155' BorderThickness='1' Cursor='Hand'>
                                    <StackPanel>
                                        <TextBlock Text='📺 Apresentação / 2K Ultra' FontWeight='Bold' FontSize='12' Foreground='#C084FC'/>
                                        <TextBlock Text='60 FPS | 28M | Max Nitidez | Toques' FontSize='10' Foreground='#94A3B8'/>
                                    </StackPanel>
                                </Button>

                                <Button Name='BtnPresetEco' Margin='3' Padding='8,8' Background='#1E293B' BorderBrush='#334155' BorderThickness='1' Cursor='Hand'>
                                    <StackPanel>
                                        <TextBlock Text='🔋 Wi-Fi Econômico' FontWeight='Bold' FontSize='12' Foreground='#94A3B8'/>
                                        <TextBlock Text='60 FPS | 6M | 720p | H.264' FontSize='10' Foreground='#64748B'/>
                                    </StackPanel>
                                </Button>
                            </UniformGrid>
                        </StackPanel>
                    </Border>

                    <!-- Section: Modo Webcam Estúdio -->
                    <Border Background='#131C2E' CornerRadius='10' BorderBrush='#1E293B' BorderThickness='1' Padding='14' Margin='0,0,0,9'>
                        <StackPanel>
                            <TextBlock Text='🎥 MODO WEBCAM ESTÚDIO' FontSize='12' FontWeight='Bold' Foreground='#EC4899' Margin='0,0,0,8'/>
                            <Button Name='BtnPresetWebcamPro' Margin='0,0,0,8' Padding='10,12' Background='#831843' BorderBrush='#BE185D' BorderThickness='1' Cursor='Hand'>
                                <StackPanel>
                                    <TextBlock Text='🎥 Ativar Modo Webcam Pro' FontWeight='Bold' FontSize='13' Foreground='#FBCFE8' HorizontalAlignment='Center'/>
                                    <TextBlock Text='1080p | 60 FPS | 90° Vertical | Sem Áudio | Sem Bordas' FontSize='11' Foreground='#F9A8D4' HorizontalAlignment='Center'/>
                                </StackPanel>
                            </Button>
                            <CheckBox Name='ChkInvisibleMode' Content='Modo Invisível / Ocultar da Área de Trabalho (Para OBS)' IsChecked='False' ToolTip='Oculta a janela colocando-a fora da tela. O OBS continua capturando sem tela preta.'/>
                        </StackPanel>
                    </Border>

                    <!-- Section: Ferramentas & Toggles Rápidos -->
                    <Border Background='#131C2E' CornerRadius='10' BorderBrush='#1E293B' BorderThickness='1' Padding='14' Margin='0,0,0,9'>
                        <StackPanel>
                            <TextBlock Text='RECURSOS E FERRAMENTAS DO SCRCPY 4.1' FontSize='12' FontWeight='Bold' Foreground='#38BDF8' Margin='0,0,0,8'/>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='*'/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column='0' Margin='0,0,6,0'>
                                    <CheckBox Name='ChkScreenOff' Content='📱 Desligar tela do celular (-S)' IsChecked='True' ToolTip='Desliga a tela física do aparelho enquanto espelha, poupando muita bateria e calor.'/>
                                    <CheckBox Name='ChkStayAwake' Content='☕ Manter aparelho acordado (-w)' IsChecked='True' ToolTip='Impede o celular de entrar em modo de suspensão durante a transmissão.'/>
                                    <CheckBox Name='ChkNoVirtualKeyboard' Content='⌨️ Ocultar Teclado Virtual (UHID)' IsChecked='True' ToolTip='Simula teclado físico de hardware (UHID) e suprime o teclado virtual (Gboard/SwiftKey) na tela do celular ao focar campos de texto, preservando 100% da visualização. Digite com o teclado físico do seu PC.'/>
                                    <CheckBox Name='ChkAudio' Content='🔊 Transmitir Áudio (Opus)' IsChecked='False' ToolTip='Encaminha o áudio do Android para o PC usando Opus de baixa latência (requer Android 11+).'/>
                                    <CheckBox Name='ChkAlwaysOnTop' Content='📌 Janela sempre no topo' IsChecked='False' ToolTip='Mantém a janela do espelhamento visível sobre todos os outros programas.'/>
                                    <CheckBox Name='ChkBorderless' Content='🔲 Janela sem bordas' IsChecked='False' ToolTip='Remove as molduras da janela para uma experiência moderna e imersiva.'/>
                                </StackPanel>
                                <StackPanel Grid.Column='1' Margin='6,0,0,0'>
                                    <CheckBox Name='ChkFullscreen' Content='🖥️ Iniciar em Tela Cheia (-f)' IsChecked='False' ToolTip='Inicia o espelhamento diretamente em modo tela cheia.'/>
                                    <CheckBox Name='ChkShowTouches' Content='👆 Exibir toques na tela (-t)' IsChecked='False' ToolTip='Mostra círculos visuais nos toques físicos na tela (ótimo para tutoriais e apresentações).'/>
                                    <CheckBox Name='ChkRecord' Content='🔴 Gravar sessão em MP4' IsChecked='False' ToolTip='Grava a transmissão diretamente em um arquivo MP4 com data e hora na pasta local.'/>
                                    <CheckBox Name='ChkCamera' Content='📷 Modo Câmera / Webcam' IsChecked='False' ToolTip='Usa a câmera física do celular como webcam de estúdio de alta resolução (requer Android 12+).'/>
                                    <CheckBox Name='ChkOtg' Content='🖱️ Modo OTG (Teclado/Mouse)' IsChecked='False' ToolTip='Controla o celular com mouse e teclado sem espelhar tela (apenas via USB).'/>
                                </StackPanel>
                            </Grid>
                        </StackPanel>
                    </Border>

                    <!-- Section: Ajustes Finos (Expander) -->
                    <Expander Background='#131C2E' BorderBrush='#1E293B' BorderThickness='1' Margin='0,0,0,9'>
                        <Expander.Header>
                            <TextBlock Text='⚙️ Ajustes Finos &amp; Parâmetros Personalizados (Clique para expandir)' FontSize='12.5' FontWeight='Bold' Foreground='#38BDF8'/>
                        </Expander.Header>
                        <Border Padding='14' Background='#0B1120' CornerRadius='0,0,8,8'>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='*'/>
                                </Grid.ColumnDefinitions>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height='Auto'/>
                                    <RowDefinition Height='Auto'/>
                                    <RowDefinition Height='Auto'/>
                                    <RowDefinition Height='Auto'/>
                                    <RowDefinition Height='Auto'/>
                                </Grid.RowDefinitions>

                                <!-- Resolução -->
                                <StackPanel Grid.Row='0' Grid.Column='0' Margin='6'>
                                    <TextBlock Text='Resolução Máxima:' FontSize='12' FontWeight='SemiBold' Foreground='#F8FAFC' Margin='0,0,0,5'/>
                                    <ComboBox Name='CmbResolution'>
                                        <ComboBoxItem Content='Original (Sem Limite)'/>
                                        <ComboBoxItem Content='2560 (2K Quad HD)'/>
                                        <ComboBoxItem Content='1920 (Full HD 1080p)' IsSelected='True'/>
                                        <ComboBoxItem Content='1600 (Equilibrado Fluido)'/>
                                        <ComboBoxItem Content='1280 (HD 720p)'/>
                                        <ComboBoxItem Content='1024 (Econômico)'/>
                                    </ComboBox>
                                </StackPanel>

                                <!-- FPS -->
                                <StackPanel Grid.Row='0' Grid.Column='1' Margin='6'>
                                    <TextBlock Text='Taxa de Quadros (FPS):' FontSize='12' FontWeight='SemiBold' Foreground='#F8FAFC' Margin='0,0,0,5'/>
                                    <ComboBox Name='CmbFps'>
                                        <ComboBoxItem Content='120 FPS (Ultra Alto)'/>
                                        <ComboBoxItem Content='90 FPS (Super Fluido)' IsSelected='True'/>
                                        <ComboBoxItem Content='60 FPS (Padrão Estável)'/>
                                        <ComboBoxItem Content='30 FPS (Econômico)'/>
                                    </ComboBox>
                                </StackPanel>

                                <!-- Bitrate -->
                                <StackPanel Grid.Row='1' Grid.Column='0' Margin='6'>
                                    <TextBlock Text='Taxa de Bits (Bitrate):' FontSize='12' FontWeight='SemiBold' Foreground='#F8FAFC' Margin='0,0,0,5'/>
                                    <ComboBox Name='CmbBitrate'>
                                        <ComboBoxItem Content='32M (Qualidade Estúdio)'/>
                                        <ComboBoxItem Content='24M (Altíssima Fidelidade)' IsSelected='True'/>
                                        <ComboBoxItem Content='16M (Fluido Recomendado)'/>
                                        <ComboBoxItem Content='10M (Padrão 1080p)'/>
                                        <ComboBoxItem Content='6M (Conexões Fracas)'/>
                                        <ComboBoxItem Content='4M (Ultra Econômico)'/>
                                    </ComboBox>
                                </StackPanel>

                                <!-- Codec -->
                                <StackPanel Grid.Row='1' Grid.Column='1' Margin='6'>
                                    <TextBlock Text='Codec de Vídeo:' FontSize='12' FontWeight='SemiBold' Foreground='#F8FAFC' Margin='0,0,0,5'/>
                                    <ComboBox Name='CmbCodec'>
                                        <ComboBoxItem Content='h265 (HEVC - Alta Eficiência)' IsSelected='True'/>
                                        <ComboBoxItem Content='av1 (Próxima Geração)'/>
                                        <ComboBoxItem Content='h264 (Compatibilidade Universal)'/>
                                    </ComboBox>
                                </StackPanel>

                                <!-- Buffer -->
                                <StackPanel Grid.Row='2' Grid.Column='0' Margin='6'>
                                    <TextBlock Text='Buffer de Vídeo (Anti-Jitter):' FontSize='12' FontWeight='SemiBold' Foreground='#F8FAFC' Margin='0,0,0,5'/>
                                    <ComboBox Name='CmbBuffer'>
                                        <ComboBoxItem Content='0 ms (Zero Latência - USB)' IsSelected='True'/>
                                        <ComboBoxItem Content='25 ms (Baixo Delay)'/>
                                        <ComboBoxItem Content='50 ms (Wi-Fi Estável)'/>
                                        <ComboBoxItem Content='80 ms (Wi-Fi com Oscilação)'/>
                                        <ComboBoxItem Content='120 ms (Alta Latência)'/>
                                    </ComboBox>
                                </StackPanel>

                                <!-- Câmera Facing -->
                                <StackPanel Grid.Row='2' Grid.Column='1' Margin='6'>
                                    <TextBlock Text='Câmera em Modo Webcam:' FontSize='12' FontWeight='SemiBold' Foreground='#F8FAFC' Margin='0,0,0,5'/>
                                    <ComboBox Name='CmbCameraFacing'>
                                        <ComboBoxItem Content='Traseira (Principal / Alta Definição)' IsSelected='True'/>
                                        <ComboBoxItem Content='Frontal (Selfie / Reuniões)'/>
                                    </ComboBox>
                                </StackPanel>

                                <!-- Orientação da Câmera -->
                                <StackPanel Grid.Row='3' Grid.Column='0' Grid.ColumnSpan='2' Margin='6'>
                                    <TextBlock Text='Orientação da Câmera (Para Tripé/Mesa):' FontSize='12' FontWeight='SemiBold' Foreground='#F8FAFC' Margin='0,0,0,5'/>
                                    <ComboBox Name='CmbCameraOrientation'>
                                        <ComboBoxItem Content='0° Padrão (Paisagem)'/>
                                        <ComboBoxItem Content='90° Vertical em Pé (Para Shorts/Reunião)' IsSelected='True'/>
                                        <ComboBoxItem Content='180° Invertido (Ponta-cabeça)'/>
                                        <ComboBoxItem Content='270° Deitado Invertido'/>
                                        <ComboBoxItem Content='Espelhado 0° (Paisagem)'/>
                                        <ComboBoxItem Content='Espelhado 90° (Vertical em Pé)'/>
                                        <ComboBoxItem Content='Espelhado 180°'/>
                                        <ComboBoxItem Content='Espelhado 270°'/>
                                    </ComboBox>
                                </StackPanel>

                                <!-- Opção de Fechar -->
                                <StackPanel Grid.Row='4' Grid.Column='0' Grid.ColumnSpan='2' Margin='6,10,6,0'>
                                    <CheckBox Name='ChkCloseOnLaunch' Content='Fechar painel após iniciar espelhamento com sucesso' IsChecked='False' ToolTip='Encerra esta janela automaticamente após lançar o SCRCPY sem erros.'/>
                                </StackPanel>
                            </Grid>
                        </Border>
                    </Expander>

                    <!-- Section: Guia Rápido de Atalhos (Expander) -->
                    <Expander Background='#131C2E' BorderBrush='#1E293B' BorderThickness='1' Margin='0,0,0,9'>
                        <Expander.Header>
                            <TextBlock Text='⌨️ Guia Rápido de Atalhos do SCRCPY (Clique para ver)' FontSize='12' FontWeight='SemiBold' Foreground='#E2E8F0'/>
                        </Expander.Header>
                        <Border Padding='14' Background='#0B1120' CornerRadius='0,0,8,8'>
                            <UniformGrid Columns='2'>
                                <TextBlock Text='• Alt + P : Ligar / Desligar tela' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + O : Apagar tela física (mantém PC)' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + K : Configurar layout teclado físico' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + F / F11 : Alternar Tela Cheia' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + H : Botão Home (Início)' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + B / Botão Dir : Botão Voltar' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + S : Alternar Apps (Multitarefa)' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + C / V : Copiar / Colar no Android' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + R : Rotacionar tela (Tempo real)' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + ←/→ : Girar a imagem 90° em tempo real (câmera vertical)' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + Shift + ←/→ : Espelhar imagem (horizontal/vertical)' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Arrastar APK : Instala app direto' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Arrastar Arquivo : Envia para o celular' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                                <TextBlock Text='• Alt + i : Exibir Contador de FPS' FontSize='11' Foreground='#CBD5E1' Margin='0,2'/>
                            </UniformGrid>
                        </Border>
                    </Expander>

                    <!-- Preview da Linha de Comando -->
                    <Expander Background='#0F172A' BorderBrush='#1E293B' BorderThickness='1' Margin='0,0,0,9'>
                        <Expander.Header>
                            <TextBlock Text='💻 Comando SCRCPY Gerado (Transparência Total)' FontSize='11' Foreground='#94A3B8'/>
                        </Expander.Header>
                        <Border Padding='6' Background='#020617' CornerRadius='0,0,8,8'>
                            <TextBox Name='TxtCmdPreview' IsReadOnly='True' Background='Transparent' Foreground='#38BDF8' FontFamily='Consolas, Courier New' FontSize='11' TextWrapping='Wrap' BorderThickness='0'/>
                        </Border>
                    </Expander>

                    <!-- Ações Principais -->
                    <Grid Margin='0,2,0,2'>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width='2*'/>
                            <ColumnDefinition Width='1*'/>
                            <ColumnDefinition Width='1*'/>
                            <ColumnDefinition Width='1*'/>
                        </Grid.ColumnDefinitions>

                        <Button Name='BtnLaunch' Grid.Column='0' Height='46' Margin='0,0,4,0' Cursor='Hand' BorderThickness='0'>
                            <Button.Background>
                                <LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
                                    <GradientStop Color='#10B981' Offset='0.0'/>
                                    <GradientStop Color='#059669' Offset='1.0'/>
                                </LinearGradientBrush>
                            </Button.Background>
                            <StackPanel Orientation='Horizontal'>
                                <TextBlock Text='🚀' FontSize='18' Margin='0,0,8,0' VerticalAlignment='Center'/>
                                <TextBlock Text='INICIAR ESPELHAMENTO' FontSize='14' FontWeight='Bold' Foreground='White' VerticalAlignment='Center'/>
                            </StackPanel>
                        </Button>

                        <Button Name='BtnQuickUsb' Grid.Column='1' Height='46' Margin='2,0,2,0' Background='#1E293B' BorderBrush='#334155' BorderThickness='1' Cursor='Hand'>
                            <StackPanel HorizontalAlignment='Center'>
                                <TextBlock Text='⚡ Direto USB' FontWeight='SemiBold' FontSize='12' Foreground='#38BDF8'/>
                                <TextBlock Text='90 FPS Zero Lag' FontSize='9' Foreground='#94A3B8'/>
                            </StackPanel>
                        </Button>

                        <Button Name='BtnStop' Grid.Column='2' Height='46' Margin='2,0,2,0' Background='#1E293B' BorderBrush='#EF4444' BorderThickness='1' Cursor='Hand'>
                            <StackPanel HorizontalAlignment='Center'>
                                <TextBlock Text='⏹️ Encerrar' FontWeight='SemiBold' FontSize='12' Foreground='#F87171'/>
                                <TextBlock Text='Parar Processos' FontSize='9' Foreground='#94A3B8'/>
                            </StackPanel>
                        </Button>

                        <Button Name='BtnToggleVisibility' Grid.Column='3' Height='46' Margin='4,0,0,0' Background='#1E293B' BorderBrush='#8B5CF6' BorderThickness='1' Cursor='Hand'>
                            <StackPanel HorizontalAlignment='Center'>
                                <TextBlock Name='TxtToggleVisibilityIcon' Text='👻' FontWeight='SemiBold' FontSize='12' Foreground='#C4B5FD' HorizontalAlignment='Center'/>
                                <TextBlock Name='TxtToggleVisibilityText' Text='Ocultar' FontSize='9' Foreground='#94A3B8' HorizontalAlignment='Center'/>
                            </StackPanel>
                        </Button>
                    </Grid>

                </StackPanel>
            </ScrollViewer>

            <!-- 3. Status Bar -->
            <Border Grid.Row='2' Background='#0F172A' CornerRadius='0,0,14,14' BorderBrush='#1E293B' BorderThickness='0,1,0,0'>
                <Grid Margin='14,0,14,0'>
                    <TextBlock Name='TxtStatus' Text='Pronto para iniciar espelhamento.' FontSize='11' Foreground='#94A3B8' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>
                    <TextBlock Text='SCRCPY 4.1 • SDL3 • H.265 / AV1' FontSize='10' Foreground='#475569' HorizontalAlignment='Right' VerticalAlignment='Center'/>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>";

            StringReader stringReader = new StringReader(xaml);
            XmlReader xmlReader = XmlReader.Create(stringReader);
            Window win = (Window)XamlReader.Load(xmlReader);

            string icoPath = Path.Combine(baseDir, "scrcpy.ico");
            if (File.Exists(icoPath))
            {
                try
                {
                    win.Icon = BitmapFrame.Create(new Uri(icoPath));
                }
                catch { }
            }

            return win;
        }

        private static void InitLogic()
        {
            // Bind Elements
            Border titleBar = (Border)mainWindow.FindName("TitleBarBorder");
            titleBar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) {
                if (e.ButtonState == MouseButtonState.Pressed) mainWindow.DragMove();
            };

            Button btnClose = (Button)mainWindow.FindName("BtnClose");
            btnClose.Click += delegate { mainWindow.Close(); };

            Button btnMin = (Button)mainWindow.FindName("BtnMinimize");
            btnMin.Click += delegate { mainWindow.WindowState = WindowState.Minimized; };

            rbUsb = (RadioButton)mainWindow.FindName("RbUsb");
            rbWifi = (RadioButton)mainWindow.FindName("RbWifi");
            panelUsb = (StackPanel)mainWindow.FindName("PanelUsb");
            panelWifi = (StackPanel)mainWindow.FindName("PanelWifi");
            txtUsbStatus = (TextBlock)mainWindow.FindName("TxtUsbStatus");
            btnRefreshUsb = (Button)mainWindow.FindName("BtnRefreshUsb");
            btnEnableTcpip = (Button)mainWindow.FindName("BtnEnableTcpip");
            txtIp = (TextBox)mainWindow.FindName("TxtIp");
            btnAutoDetectWifi = (Button)mainWindow.FindName("BtnAutoDetectWifi");
            btnTestPing = (Button)mainWindow.FindName("BtnTestPing");
            btnAdbConnect = (Button)mainWindow.FindName("BtnAdbConnect");
            btnPairWifi = (Button)mainWindow.FindName("BtnPairWifi");
            btnFixPort5555 = (Button)mainWindow.FindName("BtnFixPort5555");
            txtWifiStatus = (TextBlock)mainWindow.FindName("TxtWifiStatus");
            txtPingResult = (TextBlock)mainWindow.FindName("TxtPingResult");

            btnPresetUltra = (Button)mainWindow.FindName("BtnPresetUltra");
            btnPresetGaming = (Button)mainWindow.FindName("BtnPresetGaming");
            btnPresetBalanced = (Button)mainWindow.FindName("BtnPresetBalanced");
            btnPreset2K = (Button)mainWindow.FindName("BtnPreset2K");
            btnPresetEco = (Button)mainWindow.FindName("BtnPresetEco");
            btnPresetWebcamPro = (Button)mainWindow.FindName("BtnPresetWebcamPro");
            txtSelectedPreset = (TextBlock)mainWindow.FindName("TxtSelectedPreset");

            chkScreenOff = (CheckBox)mainWindow.FindName("ChkScreenOff");
            chkStayAwake = (CheckBox)mainWindow.FindName("ChkStayAwake");
            chkAudio = (CheckBox)mainWindow.FindName("ChkAudio");
            chkAlwaysOnTop = (CheckBox)mainWindow.FindName("ChkAlwaysOnTop");
            chkBorderless = (CheckBox)mainWindow.FindName("ChkBorderless");
            chkFullscreen = (CheckBox)mainWindow.FindName("ChkFullscreen");
            chkShowTouches = (CheckBox)mainWindow.FindName("ChkShowTouches");
            chkRecord = (CheckBox)mainWindow.FindName("ChkRecord");
            chkCamera = (CheckBox)mainWindow.FindName("ChkCamera");
            chkOtg = (CheckBox)mainWindow.FindName("ChkOtg");
            chkCloseOnLaunch = (CheckBox)mainWindow.FindName("ChkCloseOnLaunch");
            chkInvisibleMode = (CheckBox)mainWindow.FindName("ChkInvisibleMode");
            chkNoVirtualKeyboard = (CheckBox)mainWindow.FindName("ChkNoVirtualKeyboard");

            cmbResolution = (ComboBox)mainWindow.FindName("CmbResolution");
            cmbFps = (ComboBox)mainWindow.FindName("CmbFps");
            cmbBitrate = (ComboBox)mainWindow.FindName("CmbBitrate");
            cmbCodec = (ComboBox)mainWindow.FindName("CmbCodec");
            cmbBuffer = (ComboBox)mainWindow.FindName("CmbBuffer");
            cmbCameraFacing = (ComboBox)mainWindow.FindName("CmbCameraFacing");
            cmbCameraOrientation = (ComboBox)mainWindow.FindName("CmbCameraOrientation");

            btnLaunch = (Button)mainWindow.FindName("BtnLaunch");
            btnQuickUsb = (Button)mainWindow.FindName("BtnQuickUsb");
            btnStop = (Button)mainWindow.FindName("BtnStop");
            btnToggleVisibility = (Button)mainWindow.FindName("BtnToggleVisibility");
            txtToggleVisibilityIcon = (TextBlock)mainWindow.FindName("TxtToggleVisibilityIcon");
            txtToggleVisibilityText = (TextBlock)mainWindow.FindName("TxtToggleVisibilityText");
            txtStatus = (TextBlock)mainWindow.FindName("TxtStatus");
            txtCmdPreview = (TextBox)mainWindow.FindName("TxtCmdPreview");

            // Event Handlers for Connection Modes
            rbUsb.Checked += delegate {
                panelUsb.Visibility = Visibility.Visible;
                panelWifi.Visibility = Visibility.Collapsed;
                chkOtg.IsEnabled = true;
                chkOtg.ToolTip = "Controla o celular com mouse e teclado sem espelhar tela (apenas via USB).";
                UpdateCommandPreview();
            };
            rbWifi.Checked += delegate {
                panelUsb.Visibility = Visibility.Collapsed;
                panelWifi.Visibility = Visibility.Visible;
                // OTG mode is not supported over TCP/IP in scrcpy
                if (chkOtg.IsChecked == true) chkOtg.IsChecked = false;
                chkOtg.IsEnabled = false;
                chkOtg.ToolTip = "Modo OTG requer conexão física por Cabo USB.";
                UpdateCommandPreview();
                CheckWifiStatusQuickAsync();
            };

            btnRefreshUsb.Click += delegate { CheckUsbStatusAsync(false); };
            btnEnableTcpip.Click += delegate { EnableTcpipAndGetIpAsync(); };

            txtIp.TextChanged += delegate { UpdateCommandPreview(); };
            btnAutoDetectWifi.Click += delegate { AutoDetectWifiAsync(); };
            btnTestPing.Click += delegate { TestPingAsync(); };
            btnAdbConnect.Click += delegate { ConnectAdbWifiAsync(); };
            btnPairWifi.Click += delegate { ShowPairingModal(); };
            btnFixPort5555.Click += delegate { FixPort5555Async(); };

            // Presets Handlers
            btnPresetUltra.Click += delegate { ApplyPreset("Ultra"); };
            btnPresetGaming.Click += delegate { ApplyPreset("Gaming"); };
            btnPresetBalanced.Click += delegate { ApplyPreset("Balanced"); };
            btnPreset2K.Click += delegate { ApplyPreset("2K"); };
            btnPresetEco.Click += delegate { ApplyPreset("Eco"); };
            btnPresetWebcamPro.Click += delegate { ApplyPreset("WebcamPro"); };

            // Handlers for manual tweaks that switch to Custom Preset
            RoutedEventHandler tweakHandler = delegate {
                if (!isApplyingPreset)
                {
                    MarkCustomPreset();
                }
                UpdateCommandPreview();
            };

            chkScreenOff.Click += tweakHandler;
            chkStayAwake.Click += tweakHandler;
            chkAudio.Click += tweakHandler;
            chkAlwaysOnTop.Click += tweakHandler;
            chkBorderless.Click += tweakHandler;
            chkFullscreen.Click += tweakHandler;
            chkShowTouches.Click += tweakHandler;
            chkRecord.Click += tweakHandler;
            chkCamera.Click += tweakHandler;
            chkOtg.Click += tweakHandler;
            chkInvisibleMode.Click += tweakHandler;
            chkNoVirtualKeyboard.Click += tweakHandler;
            chkCloseOnLaunch.Click += delegate { SaveSettings(); };

            SelectionChangedEventHandler comboHandler = delegate {
                if (!isApplyingPreset)
                {
                    MarkCustomPreset();
                }
                UpdateCommandPreview();
            };

            cmbResolution.SelectionChanged += comboHandler;
            cmbFps.SelectionChanged += comboHandler;
            cmbBitrate.SelectionChanged += comboHandler;
            cmbCodec.SelectionChanged += comboHandler;
            cmbBuffer.SelectionChanged += comboHandler;
            cmbCameraFacing.SelectionChanged += comboHandler;
            cmbCameraOrientation.SelectionChanged += comboHandler;

            // Launchers & Actions
            btnLaunch.Click += delegate { LaunchScrcpy(); };
            btnQuickUsb.Click += delegate {
                rbUsb.IsChecked = true;
                ApplyPreset("Ultra");
                LaunchScrcpy();
            };
            btnStop.Click += delegate { StopScrcpy(); };
            btnToggleVisibility.Click += delegate { ToggleScrcpyVisibility(); };

            mainWindow.Closed += delegate {
                SaveSettings();
                if (autoDetectTimer != null) autoDetectTimer.Stop();
                try {
                    if (chkNoVirtualKeyboard != null && chkNoVirtualKeyboard.IsChecked == true)
                    {
                        RunCommand(adbPath, "shell settings put secure show_ime_with_hard_keyboard 1", 1500);
                    }
                } catch { }
            };
        }

        private static void ResetPresetButtons()
        {
            SolidColorBrush bgDefault = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // #1E293B
            SolidColorBrush borderDefault = new SolidColorBrush(Color.FromRgb(51, 65, 85)); // #334155

            btnPresetUltra.Background = bgDefault;
            btnPresetUltra.BorderBrush = borderDefault;

            btnPresetGaming.Background = bgDefault;
            btnPresetGaming.BorderBrush = borderDefault;

            btnPresetBalanced.Background = bgDefault;
            btnPresetBalanced.BorderBrush = borderDefault;

            btnPreset2K.Background = bgDefault;
            btnPreset2K.BorderBrush = borderDefault;

            btnPresetEco.Background = bgDefault;
            btnPresetEco.BorderBrush = borderDefault;

            btnPresetWebcamPro.Background = new SolidColorBrush(Color.FromRgb(131, 24, 67));
            btnPresetWebcamPro.BorderBrush = new SolidColorBrush(Color.FromRgb(190, 24, 93));
        }

        private static void MarkCustomPreset()
        {
            currentPresetName = "Custom";
            txtSelectedPreset.Text = "Perfil: 🛠️ Personalizado";
            txtSelectedPreset.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)); // Cyan
            ResetPresetButtons();
        }

        private static void ApplyPreset(string preset)
        {
            isApplyingPreset = true;
            currentPresetName = preset;
            ResetPresetButtons();

            switch (preset)
            {
                case "Ultra":
                    txtSelectedPreset.Text = "Perfil: Ultra Competitivo";
                    txtSelectedPreset.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    btnPresetUltra.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
                    btnPresetUltra.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));

                    cmbFps.SelectedIndex = 1; // 90 FPS
                    cmbBitrate.SelectedIndex = 1; // 24M
                    cmbResolution.SelectedIndex = 2; // 1920
                    cmbCodec.SelectedIndex = 0; // h265
                    cmbBuffer.SelectedIndex = 0; // 0ms
                    chkScreenOff.IsChecked = true;
                    chkStayAwake.IsChecked = true;
                    chkAudio.IsChecked = false;
                    chkFullscreen.IsChecked = false;
                    chkShowTouches.IsChecked = false;
                    chkCamera.IsChecked = false;
                    chkOtg.IsChecked = false;
                    chkNoVirtualKeyboard.IsChecked = true;
                    break;

                case "Gaming":
                    txtSelectedPreset.Text = "Perfil: Gaming Fluido";
                    txtSelectedPreset.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                    btnPresetGaming.Background = new SolidColorBrush(Color.FromRgb(12, 74, 110));
                    btnPresetGaming.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));

                    cmbFps.SelectedIndex = 1; // 90 FPS
                    cmbBitrate.SelectedIndex = 2; // 16M
                    cmbResolution.SelectedIndex = 3; // 1600
                    cmbCodec.SelectedIndex = 0; // h265
                    cmbBuffer.SelectedIndex = 1; // 25ms
                    chkScreenOff.IsChecked = true;
                    chkStayAwake.IsChecked = true;
                    chkAudio.IsChecked = true;
                    chkFullscreen.IsChecked = false;
                    chkShowTouches.IsChecked = false;
                    chkCamera.IsChecked = false;
                    chkOtg.IsChecked = false;
                    chkNoVirtualKeyboard.IsChecked = true;
                    break;

                case "Balanced":
                    txtSelectedPreset.Text = "Perfil: Equilibrado Diário";
                    txtSelectedPreset.Foreground = new SolidColorBrush(Color.FromRgb(252, 211, 77));
                    btnPresetBalanced.Background = new SolidColorBrush(Color.FromRgb(69, 26, 3));
                    btnPresetBalanced.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 211, 77));

                    cmbFps.SelectedIndex = 2; // 60 FPS
                    cmbBitrate.SelectedIndex = 3; // 10M
                    cmbResolution.SelectedIndex = 2; // 1920
                    cmbCodec.SelectedIndex = 0; // h265
                    cmbBuffer.SelectedIndex = 2; // 50ms
                    chkScreenOff.IsChecked = true;
                    chkStayAwake.IsChecked = true;
                    chkAudio.IsChecked = true;
                    chkFullscreen.IsChecked = false;
                    chkShowTouches.IsChecked = false;
                    chkCamera.IsChecked = false;
                    chkOtg.IsChecked = false;
                    chkNoVirtualKeyboard.IsChecked = true;
                    break;

                case "2K":
                    txtSelectedPreset.Text = "Perfil: Apresentação / 2K Ultra";
                    txtSelectedPreset.Foreground = new SolidColorBrush(Color.FromRgb(192, 132, 252));
                    btnPreset2K.Background = new SolidColorBrush(Color.FromRgb(59, 7, 100));
                    btnPreset2K.BorderBrush = new SolidColorBrush(Color.FromRgb(192, 132, 252));

                    cmbFps.SelectedIndex = 2; // 60 FPS
                    cmbBitrate.SelectedIndex = 0; // 32M
                    cmbResolution.SelectedIndex = 1; // 2560
                    cmbCodec.SelectedIndex = 0; // h265
                    cmbBuffer.SelectedIndex = 2; // 50ms
                    chkScreenOff.IsChecked = false;
                    chkStayAwake.IsChecked = true;
                    chkAudio.IsChecked = true;
                    chkFullscreen.IsChecked = false;
                    chkShowTouches.IsChecked = true; // Mostra toques para apresentação
                    chkCamera.IsChecked = false;
                    chkOtg.IsChecked = false;
                    chkNoVirtualKeyboard.IsChecked = true;
                    break;

                case "Eco":
                    txtSelectedPreset.Text = "Perfil: Wi-Fi Econômico";
                    txtSelectedPreset.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    btnPresetEco.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                    btnPresetEco.BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));

                    cmbFps.SelectedIndex = 2; // 60 FPS
                    cmbBitrate.SelectedIndex = 4; // 6M
                    cmbResolution.SelectedIndex = 4; // 1280
                    cmbCodec.SelectedIndex = 2; // h264
                    cmbBuffer.SelectedIndex = 3; // 80ms
                    chkScreenOff.IsChecked = true;
                    chkStayAwake.IsChecked = true;
                    chkAudio.IsChecked = false;
                    chkFullscreen.IsChecked = false;
                    chkShowTouches.IsChecked = false;
                    chkCamera.IsChecked = false;
                    chkOtg.IsChecked = false;
                    chkNoVirtualKeyboard.IsChecked = true;
                    break;

                case "WebcamPro":
                    txtSelectedPreset.Text = "Perfil: 🎥 Webcam Pro Estúdio";
                    txtSelectedPreset.Foreground = new SolidColorBrush(Color.FromRgb(244, 114, 182));
                    btnPresetWebcamPro.Background = new SolidColorBrush(Color.FromRgb(157, 23, 77));
                    btnPresetWebcamPro.BorderBrush = new SolidColorBrush(Color.FromRgb(244, 114, 182));

                    cmbFps.SelectedIndex = 2; // 60 FPS
                    cmbBitrate.SelectedIndex = 1; // 24M (high quality)
                    cmbResolution.SelectedIndex = 2; // 1080p
                    cmbCodec.SelectedIndex = 0; // h265
                    cmbBuffer.SelectedIndex = 0; // 0ms (Zero delay)
                    cmbCameraFacing.SelectedIndex = 0; // Traseira
                    cmbCameraOrientation.SelectedIndex = 1; // 90° Vertical

                    chkScreenOff.IsChecked = false; // Proteger contra -S
                    chkStayAwake.IsChecked = true;
                    chkAudio.IsChecked = false;
                    chkFullscreen.IsChecked = false;
                    chkShowTouches.IsChecked = false;
                    chkCamera.IsChecked = true;
                    chkOtg.IsChecked = false;
                    chkBorderless.IsChecked = true;
                    chkInvisibleMode.IsChecked = false;
                    chkNoVirtualKeyboard.IsChecked = true;
                    break;
            }

            isApplyingPreset = false;
            UpdateCommandPreview();
        }

        private static string BuildArguments()
        {
            bool isUsb = rbUsb.IsChecked == true;
            string selector = "";
            if (isUsb)
            {
                if (!string.IsNullOrEmpty(lastDetectedUsbDevice))
                {
                    selector = "-s " + lastDetectedUsbDevice;
                }
                else
                {
                    selector = "-d";
                }
            }
            else
            {
                string targetIp = txtIp.Text.Trim();
                if (!string.IsNullOrEmpty(targetIp))
                {
                    if (!targetIp.Contains(":")) targetIp += ":5555";
                    selector = "-s " + targetIp;
                }
            }

            return BuildArgumentsForTarget(selector, isUsb);
        }

        private static string BuildArgumentsForTarget(string selector, bool isUsb)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(selector))
            {
                sb.Append(selector + " ");
            }

            // OTG Mode (USB Only)
            if (isUsb && chkOtg.IsChecked == true)
            {
                sb.Append("--otg ");
                return sb.ToString().Trim();
            }

            // Camera / Webcam Mode
            if (chkCamera.IsChecked == true)
            {
                sb.Append("--video-source=camera ");
                if (cmbCameraFacing.SelectedIndex == 1)
                {
                    sb.Append("--camera-facing=front ");
                }
                else
                {
                    sb.Append("--camera-facing=back ");
                }

                int oriIdx = cmbCameraOrientation.SelectedIndex;
                string[] oriValues = { "0", "90", "180", "270", "flip0", "flip90", "flip180", "flip270" };
                if (oriIdx < 0 || oriIdx >= oriValues.Length) oriIdx = 0;
                sb.Append("--capture-orientation=" + oriValues[oriIdx] + " ");
            }

            if (chkScreenOff.IsChecked == true && chkCamera.IsChecked != true) sb.Append("-S ");
            if (chkStayAwake.IsChecked == true) sb.Append("-w ");
            if (chkNoVirtualKeyboard.IsChecked == true && chkCamera.IsChecked != true) sb.Append("--keyboard=uhid ");
            if (chkAlwaysOnTop.IsChecked == true) sb.Append("--always-on-top ");
            if (chkBorderless.IsChecked == true) sb.Append("--window-borderless ");
            if (chkFullscreen.IsChecked == true) sb.Append("-f ");
            if (chkShowTouches.IsChecked == true) sb.Append("-t ");

            if (chkAudio.IsChecked == false)
            {
                sb.Append("--no-audio ");
            }
            else
            {
                sb.Append("--audio-codec=opus ");
            }

            if (chkRecord.IsChecked == true)
            {
                string recordFile = Path.Combine(baseDir, "gravacao_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
                sb.Append("--record=\"" + recordFile + "\" ");
            }

            // Codec
            string codec = "h265";
            if (cmbCodec.SelectedIndex == 1) codec = "av1";
            else if (cmbCodec.SelectedIndex == 2) codec = "h264";
            sb.Append("--video-codec=" + codec + " ");

            // Bitrate
            string bitrate = "24M";
            if (cmbBitrate.SelectedIndex == 0) bitrate = "32M";
            else if (cmbBitrate.SelectedIndex == 1) bitrate = "24M";
            else if (cmbBitrate.SelectedIndex == 2) bitrate = "16M";
            else if (cmbBitrate.SelectedIndex == 3) bitrate = "10M";
            else if (cmbBitrate.SelectedIndex == 4) bitrate = "6M";
            else if (cmbBitrate.SelectedIndex == 5) bitrate = "4M";
            sb.Append("--video-bit-rate=" + bitrate + " ");

            // Resolution
            if (cmbResolution.SelectedIndex == 1) sb.Append("--max-size=2560 ");
            else if (cmbResolution.SelectedIndex == 2) sb.Append("--max-size=1920 ");
            else if (cmbResolution.SelectedIndex == 3) sb.Append("--max-size=1600 ");
            else if (cmbResolution.SelectedIndex == 4) sb.Append("--max-size=1280 ");
            else if (cmbResolution.SelectedIndex == 5) sb.Append("--max-size=1024 ");

            // FPS
            if (cmbFps.SelectedIndex == 0) sb.Append("--max-fps=120 ");
            else if (cmbFps.SelectedIndex == 1) sb.Append("--max-fps=90 ");
            else if (cmbFps.SelectedIndex == 2) sb.Append("--max-fps=60 ");
            else if (cmbFps.SelectedIndex == 3) sb.Append("--max-fps=30 ");

            // Buffer
            if (cmbBuffer.SelectedIndex == 0) sb.Append("--video-buffer=0 ");
            else if (cmbBuffer.SelectedIndex == 1) sb.Append("--video-buffer=25 ");
            else if (cmbBuffer.SelectedIndex == 2) sb.Append("--video-buffer=50 ");
            else if (cmbBuffer.SelectedIndex == 3) sb.Append("--video-buffer=80 ");
            else if (cmbBuffer.SelectedIndex == 4) sb.Append("--video-buffer=120 ");

            sb.Append("--shortcut-mod=lalt,lctrl ");
            string title = isUsb ? "Aura - USB (Zero Delay)" : "Aura - Wi-Fi (Fluido)";
            if (chkCamera.IsChecked == true)
            {
                title = "Aura Webcam Pro";
            }
            sb.Append("--window-title=\"" + title + "\" ");

            if (chkInvisibleMode.IsChecked == true)
            {
                sb.Append("--window-x=-32000 --window-y=-32000 ");
            }

            return sb.ToString().Trim();
        }

        private static void UpdateCommandPreview()
        {
            if (txtCmdPreview == null) return;
            string args = BuildArguments();
            txtCmdPreview.Text = "scrcpy.exe " + args;
        }

        private static void CheckUsbStatusAsync(bool silent)
        {
            if (isCheckingUsb) return;
            isCheckingUsb = true;

            if (!silent && txtUsbStatus != null)
            {
                txtUsbStatus.Text = "Detectando via ADB...";
                txtUsbStatus.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21)); // Yellow
            }

            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    string output = RunCommand(adbPath, "devices -l", 3500);
                    string detectedDevice = null;
                    string deviceState = null;
                    string deviceModel = null;

                    string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.StartsWith("List of") || string.IsNullOrEmpty(t)) continue;

                        string[] parts = Regex.Split(t, @"\s+");
                        if (parts.Length >= 2)
                        {
                            string serial = parts[0];
                            string state = parts[1];

                            bool isNetwork = serial.Contains(":") || serial.StartsWith("adb-", StringComparison.OrdinalIgnoreCase) || serial.Contains("._tcp");

                            // Match physical USB device (does not contain network identifiers)
                            if (!isNetwork && detectedDevice == null)
                            {
                                detectedDevice = serial;
                                deviceState = state;

                                Match m = Regex.Match(t, @"model:(\S+)");
                                if (m.Success)
                                {
                                    deviceModel = m.Groups[1].Value.Replace("_", " ");
                                }
                            }
                        }
                    }

                    SafeInvoke(delegate {
                        if (detectedDevice != null)
                        {
                            lastDetectedUsbDevice = detectedDevice;
                            lastDetectedUsbState = deviceState;

                            if (deviceState == "device")
                            {
                                string label = string.IsNullOrEmpty(deviceModel) ? detectedDevice : deviceModel + " (" + detectedDevice + ")";
                                txtUsbStatus.Text = "🟢 Conectado: " + label;
                                txtUsbStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Emerald
                                if (!silent)
                                {
                                    txtStatus.Text = "Dispositivo USB pronto para espelhar a 90 FPS com Latência Zero.";
                                }
                            }
                            else if (deviceState == "unauthorized")
                            {
                                txtUsbStatus.Text = "🟠 Não Autorizado: " + detectedDevice + " (Permita no celular)";
                                txtUsbStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 146, 60)); // Orange
                                if (!silent)
                                {
                                    txtStatus.Text = "Aparelho detectado, mas não autorizado. Marque 'Permitir depuração USB' na tela.";
                                }
                            }
                            else
                            {
                                txtUsbStatus.Text = "🔴 Estado: " + deviceState + " (" + detectedDevice + ")";
                                txtUsbStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                            }
                        }
                        else
                        {
                            lastDetectedUsbDevice = null;
                            lastDetectedUsbState = null;
                            txtUsbStatus.Text = "⚪ Nenhum aparelho USB detectado (Conecte o cabo USB)";
                            txtUsbStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Gray
                        }
                    });
                }
                catch { }
                finally
                {
                    isCheckingUsb = false;
                }
            });
        }

        private static List<string> GetLocalSubnetPrefixes()
        {
            List<string> prefixes = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        IPInterfaceProperties props = ni.GetIPProperties();
                        foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                string ip = addr.Address.ToString();
                                int lastDot = ip.LastIndexOf('.');
                                if (lastDot > 0)
                                {
                                    prefixes.Add(ip.Substring(0, lastDot + 1));
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return prefixes;
        }

        private static string QueryDeviceWifiIp(string serial)
        {
            try
            {
                string target = !string.IsNullOrEmpty(serial) ? "-s " + serial : "-d";
                string ipRes1 = RunCommand(adbPath, target + " shell ip -f inet addr show wlan0", 3000);
                Match m1 = Regex.Match(ipRes1, @"inet\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                if (m1.Success) return m1.Groups[1].Value;

                string ipRes2 = RunCommand(adbPath, target + " shell ip route show dev wlan0", 3000);
                Match m2 = Regex.Match(ipRes2, @"src\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                if (m2.Success) return m2.Groups[1].Value;

                string propRes = RunCommand(adbPath, target + " shell getprop dhcp.wlan0.ipaddress", 2000).Trim();
                if (Regex.IsMatch(propRes, @"^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$")) return propRes;
            }
            catch { }
            return null;
        }

        private static string DiscoverMdnsConnectEndpoint(List<string> localPrefixes, string preferredIp)
        {
            string mdnsOut = RunCommand(adbPath, "mdns services", 3500);
            string[] lines = mdnsOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            string bestCandidate = null;
            int bestScore = int.MinValue;
            string anyDiscoveredPort = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("List of") || string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.Contains("_adb-tls-connect") || trimmed.Contains("_adb._tcp"))
                {
                    Match m = Regex.Match(trimmed, @"([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}):([0-9]{2,5})");
                    if (m.Success)
                    {
                        string ip = m.Groups[1].Value;
                        string port = m.Groups[2].Value;
                        anyDiscoveredPort = port;

                        int score = 10;
                        if (!string.IsNullOrEmpty(preferredIp) && ip == preferredIp)
                        {
                            score += 1000;
                        }
                        if (localPrefixes != null)
                        {
                            foreach (string pfx in localPrefixes)
                            {
                                if (ip.StartsWith(pfx)) { score += 500; break; }
                            }
                        }
                        if (ip.StartsWith("10.131.") || ip.StartsWith("172.22."))
                        {
                            score -= 300;
                        }
                        if (ip.StartsWith("192.168."))
                        {
                            score += 100;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCandidate = ip + ":" + port;
                        }
                    }
                }
            }

            // Se encontrou uma porta de Wireless Debugging no mDNS, mas o IP anunciado foi de interface virtual/USB,
            // vincula essa porta com o IP real da interface Wi-Fi do celular
            if (!string.IsNullOrEmpty(preferredIp) && !string.IsNullOrEmpty(anyDiscoveredPort))
            {
                if (bestCandidate == null || !bestCandidate.StartsWith(preferredIp + ":"))
                {
                    return preferredIp + ":" + anyDiscoveredPort;
                }
            }

            return bestCandidate;
        }

        private static void EnableTcpipAndGetIpAsync()
        {
            txtStatus.Text = "Verificando dispositivo USB para habilitar TCP/IP...";

            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    if (lastDetectedUsbState != "device")
                    {
                        SafeInvoke(delegate {
                            MessageBox.Show("Nenhum aparelho Android autorizado foi detectado via USB.\n\nPor favor, conecte o cabo USB ao computador e ao celular e confirme a permissão de Depuração USB antes de ativar o modo Wi-Fi.", "Aparelho USB Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                            txtStatus.Text = "Conecte o cabo USB para habilitar o modo Wi-Fi.";
                        });
                        return;
                    }

                    SafeInvoke(delegate {
                        txtStatus.Text = "Habilitando modo TCP/IP na porta 5555 via ADB...";
                    });

                    string usbTarget = !string.IsNullOrEmpty(lastDetectedUsbDevice) ? "-s " + lastDetectedUsbDevice : "-d";
                    RunCommand(adbPath, usbTarget + " tcpip 5555", 5000);

                    string detectedIp = QueryDeviceWifiIp(lastDetectedUsbDevice);
                    if (string.IsNullOrEmpty(detectedIp))
                    {
                        string ipRes1 = RunCommand(adbPath, usbTarget + " shell ip -f inet addr show wlan0", 3500);
                        Match m1 = Regex.Match(ipRes1, @"inet\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                        if (m1.Success) detectedIp = m1.Groups[1].Value;
                    }

                    SafeInvoke(delegate {
                        if (!string.IsNullOrEmpty(detectedIp))
                        {
                            string fullTarget = detectedIp + ":5555";
                            txtIp.Text = fullTarget;
                            try { File.WriteAllText(lastIpFile, fullTarget); } catch { }
                            rbWifi.IsChecked = true;
                            SaveSettings();

                            // Conecta imediatamente via Wi-Fi em background
                            ThreadPool.QueueUserWorkItem(delegate {
                                try
                                {
                                    string connRes = RunCommand(adbPath, "connect " + fullTarget, 5000);
                                    bool ok = connRes.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || connRes.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0;

                                    SafeInvoke(delegate {
                                        if (ok)
                                        {
                                            txtStatus.Text = "🟢 Modo Wi-Fi ativo e conectado: " + fullTarget;
                                            if (txtWifiStatus != null)
                                            {
                                                txtWifiStatus.Text = "🟢 Conectado na porta 5555: " + fullTarget;
                                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                                            }
                                            TestPingAsync();
                                            MessageBox.Show("Modo Wi-Fi (TCP/IP 5555) ativado e conectado com sucesso!\n\nEndereço: " + fullTarget + "\n\nVocê já pode desconectar o cabo USB e clicar em 'INICIAR ESPELHAMENTO'.", "Modo Wi-Fi Ativado", MessageBoxButton.OK, MessageBoxImage.Information);
                                        }
                                        else
                                        {
                                            txtStatus.Text = "Porta 5555 aberta em " + fullTarget + " (" + connRes.Trim() + ")";
                                            TestPingAsync();
                                        }
                                    });
                                }
                                catch { }
                            });
                        }
                        else
                        {
                            rbWifi.IsChecked = true;
                            txtStatus.Text = "Porta 5555 liberada! Digite o IP do Wi-Fi na caixa acima.";
                            MessageBox.Show("Modo TCP/IP liberado na porta 5555 com sucesso!\n\nNão foi possível obter o IP do Wi-Fi automaticamente. Insira o IP do seu celular seguido de :5555 manualmente.", "Modo Wi-Fi Liberado", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                catch { }
            });
        }

        private static void ConnectAdbWifiAsync()
        {
            string raw = txtIp.Text.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                MessageBox.Show("Por favor, digite o endereço IP e a porta do celular.", "IP Vazio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            txtStatus.Text = "Conectando ADB via Wi-Fi...";
            if (txtPingResult != null)
            {
                txtPingResult.Text = "⏳ Conectando ADB em " + raw + "...";
                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            }

            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    string target = raw;
                    bool connected = false;
                    string output = "";

                    if (!target.Contains(":"))
                    {
                        // Tenta 1: Porta 5555
                        string t5555 = target + ":5555";
                        output = RunCommand(adbPath, "connect " + t5555, 3500);
                        if (output.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            target = t5555;
                            connected = true;
                        }
                        else
                        {
                            // Tenta 2: Porta dinamica descoberta no mDNS
                            List<string> pfx = GetLocalSubnetPrefixes();
                            string mdnsTarget = DiscoverMdnsConnectEndpoint(pfx, target);
                            if (!string.IsNullOrEmpty(mdnsTarget))
                            {
                                output = RunCommand(adbPath, "connect " + mdnsTarget, 3500);
                                if (output.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    target = mdnsTarget;
                                    connected = true;
                                }
                            }
                        }

                        // Tenta 3: Se USB estiver conectado, ativa tcpip 5555 automaticamente
                        if (!connected && lastDetectedUsbState == "device" && !string.IsNullOrEmpty(lastDetectedUsbDevice))
                        {
                            RunCommand(adbPath, "-s " + lastDetectedUsbDevice + " tcpip 5555", 3500);
                            string t5555Usb = (target.Contains(":") ? target.Split(':')[0] : target) + ":5555";
                            output = RunCommand(adbPath, "connect " + t5555Usb, 3500);
                            if (output.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                target = t5555Usb;
                                connected = true;
                            }
                        }

                        if (!connected && !target.Contains(":")) target = target + ":5555";
                    }
                    else
                    {
                        output = RunCommand(adbPath, "connect " + target, 5000);
                        connected = output.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0;

                        // Se falhou na porta 5555, tenta buscar porta dinamica ativa no mDNS
                        if (!connected && target.EndsWith(":5555"))
                        {
                            string ipOnly = target.Split(':')[0];
                            List<string> pfx = GetLocalSubnetPrefixes();
                            string mdnsTarget = DiscoverMdnsConnectEndpoint(pfx, ipOnly);
                            if (!string.IsNullOrEmpty(mdnsTarget) && mdnsTarget != target)
                            {
                                string out2 = RunCommand(adbPath, "connect " + mdnsTarget, 3500);
                                if (out2.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || out2.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    target = mdnsTarget;
                                    connected = true;
                                    output = out2;
                                }
                            }
                        }

                        // Fallback USB: Se ainda não conectou e USB estiver conectado, ativa tcpip 5555
                        if (!connected && lastDetectedUsbState == "device" && !string.IsNullOrEmpty(lastDetectedUsbDevice))
                        {
                            RunCommand(adbPath, "-s " + lastDetectedUsbDevice + " tcpip 5555", 3500);
                            string ipOnly = target.Split(':')[0];
                            string t5555Usb = ipOnly + ":5555";
                            string outUsb = RunCommand(adbPath, "connect " + t5555Usb, 3500);
                            if (outUsb.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || outUsb.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                target = t5555Usb;
                                connected = true;
                                output = outUsb;
                            }
                        }
                    }

                    string finalTarget = target;
                    bool finalConnected = connected;
                    string finalOutput = output;

                    SafeInvoke(delegate {
                        txtIp.Text = finalTarget;
                        if (finalConnected)
                        {
                            txtStatus.Text = "🟢 ADB Wi-Fi Conectado com sucesso em " + finalTarget + "!";
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "🟢 Conectado: " + finalTarget;
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                            }
                            try { File.WriteAllText(lastIpFile, finalTarget); } catch { }
                            SaveSettings();
                            TestPingAsync();
                            MessageBox.Show("Aparelho conectado via ADB Wi-Fi com sucesso!\n\nEndereço: " + finalTarget + "\n\nAgora você já pode clicar em 'INICIAR ESPELHAMENTO'.", "Conexão Estabelecida", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            txtStatus.Text = "❌ Falha ao conectar ADB Wi-Fi em " + finalTarget;
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "🔴 Falha na conexão";
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                            }
                            if (txtPingResult != null)
                            {
                                txtPingResult.Text = "❌ Não foi possível conectar a " + finalTarget + ".";
                                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                            }

                            MessageBox.Show("Não foi possível conectar via ADB Wi-Fi ao endereço:\n" + finalTarget + "\n\nRetorno do ADB:\n" + finalOutput.Trim() + "\n\n💡 Diagnóstico e Soluções:\n1. Se você ativou 'Depuração por Wi-Fi' no Android 11+, a porta NÃO é 5555! Clique no botão '🔍 Auto-Detectar' para capturar a porta dinâmica atual.\n2. Se for a primeira conexão sem fio entre este computador e o celular, clique em '🔑 Parear (Android 11+)' e informe o código de 6 dígitos.\n3. Se o cabo USB estiver conectado, clique em '📲 Ativar Wi-Fi (TCP/IP)' para liberar o modo sem fio automaticamente.\n4. Certifique-se de que o computador e o smartphone estão conectados no mesmo roteador Wi-Fi.", "Falha de Conexão Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    });
                }
                catch { }
            });
        }

        private static void AutoDetectWifiAsync()
        {
            if (isAutoDetecting) return;
            isAutoDetecting = true;

            txtStatus.Text = "🔍 Buscando celulares com Depuração Wi-Fi na rede...";
            if (txtPingResult != null)
            {
                txtPingResult.Text = "⏳ Escaneando rede sem fio (mDNS / ADB)...";
                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            }

            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    List<string> localPrefixes = GetLocalSubnetPrefixes();
                    string preferredIp = null;
                    if (lastDetectedUsbState == "device" && !string.IsNullOrEmpty(lastDetectedUsbDevice))
                    {
                        preferredIp = QueryDeviceWifiIp(lastDetectedUsbDevice);
                    }

                    string discoveredTarget = DiscoverMdnsConnectEndpoint(localPrefixes, preferredIp);
                    string discoverySource = "mDNS (Depuração Wi-Fi)";

                    // Se não localizou no mDNS na primeira consulta rápida, aguarda 500ms e tenta novamente
                    if (string.IsNullOrEmpty(discoveredTarget))
                    {
                        Thread.Sleep(500);
                        discoveredTarget = DiscoverMdnsConnectEndpoint(localPrefixes, preferredIp);
                    }

                    // 2. Verifica se já há dispositivo TCP conectado no ADB
                    if (string.IsNullOrEmpty(discoveredTarget))
                    {
                        string devOut = RunCommand(adbPath, "devices", 2500);
                        string[] devLines = devOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in devLines)
                        {
                            string t = line.Trim();
                            if (t.StartsWith("List of") || string.IsNullOrEmpty(t)) continue;
                            string[] parts = Regex.Split(t, @"\s+");
                            if (parts.Length >= 2 && parts[1] == "device" && parts[0].Contains(":"))
                            {
                                discoveredTarget = parts[0];
                                discoverySource = "Dispositivo ADB ativo";
                                break;
                            }
                        }
                    }

                    // 3. Fallback: Se USB estiver conectado, ativa tcpip 5555 e obtém IP do Wi-Fi
                    if (string.IsNullOrEmpty(discoveredTarget) && lastDetectedUsbState == "device" && !string.IsNullOrEmpty(lastDetectedUsbDevice))
                    {
                        RunCommand(adbPath, "-s " + lastDetectedUsbDevice + " tcpip 5555", 3500);
                        if (string.IsNullOrEmpty(preferredIp)) preferredIp = QueryDeviceWifiIp(lastDetectedUsbDevice);
                        if (!string.IsNullOrEmpty(preferredIp))
                        {
                            discoveredTarget = preferredIp + ":5555";
                            discoverySource = "USB (TCP/IP 5555)";
                        }
                    }

                    SafeInvoke(delegate {
                        if (!string.IsNullOrEmpty(discoveredTarget))
                        {
                            txtIp.Text = discoveredTarget;
                            try { File.WriteAllText(lastIpFile, discoveredTarget); } catch { }
                            SaveSettings();
                            txtStatus.Text = "🟢 Aparelho Wi-Fi localizado via " + discoverySource + ": " + discoveredTarget;
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "🟢 Encontrado: " + discoveredTarget;
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                            }
                            if (txtPingResult != null)
                            {
                                txtPingResult.Text = "🟢 Localizado (" + discoverySource + "): " + discoveredTarget + " - Conectando ADB...";
                                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                            }

                            ConnectAdbWifiBackground(discoveredTarget);
                        }
                        else
                        {
                            txtStatus.Text = "⚠️ Nenhum aparelho Wi-Fi detectado automaticamente.";
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "⚠️ Não detectado";
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 146, 60));
                            }
                            if (txtPingResult != null)
                            {
                                txtPingResult.Text = "⚠️ Nenhum serviço mDNS encontrado. Veja as instruções.";
                                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(251, 146, 60));
                            }
                            MessageBox.Show("Nenhum aparelho com Depuração Wi-Fi foi encontrado automaticamente na rede.\n\nVerifique:\n1. O celular está conectado na mesma rede Wi-Fi que o PC?\n2. Nas Opções do Desenvolvedor, o botão 'Depuração por Wi-Fi' está ATIVADO?\n3. Se for a primeira conexão sem fio, use o botão '🔑 Parear (Android 11+)' com o código de 6 dígitos.\n4. Ou digite o IP e a Porta que aparecem na tela do seu celular.", "Aparelho Wi-Fi Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                catch { }
                finally
                {
                    isAutoDetecting = false;
                }
            });
        }

        private static void ConnectAdbWifiBackground(string target)
        {
            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    string output = RunCommand(adbPath, "connect " + target, 5000);
                    bool connected = output.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0;

                    SafeInvoke(delegate {
                        if (connected)
                        {
                            txtStatus.Text = "🟢 ADB Wi-Fi Conectado em " + target + "! Pronto para Iniciar.";
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "🟢 Conectado: " + target;
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                            }
                            try { File.WriteAllText(lastIpFile, target); } catch { }
                            SaveSettings();
                            TestPingAsync();
                        }
                        else
                        {
                            txtStatus.Text = "⚠️ Detectado " + target + ", mas não conectou (" + output.Trim() + ")";
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "🟡 Detectado, mas requer pareamento";
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
                            }
                        }
                    });
                }
                catch { }
            });
        }

        private static void CheckWifiStatusQuickAsync()
        {
            if (isCheckingWifi) return;
            isCheckingWifi = true;

            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    List<string> localPrefixes = GetLocalSubnetPrefixes();
                    string preferredIp = null;
                    if (lastDetectedUsbState == "device" && !string.IsNullOrEmpty(lastDetectedUsbDevice))
                    {
                        preferredIp = QueryDeviceWifiIp(lastDetectedUsbDevice);
                    }

                    string foundTarget = DiscoverMdnsConnectEndpoint(localPrefixes, preferredIp);

                    string devOut = RunCommand(adbPath, "devices", 2000);
                    string connectedTarget = null;
                    string[] devLines = devOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in devLines)
                    {
                        string t = line.Trim();
                        if (t.StartsWith("List of") || string.IsNullOrEmpty(t)) continue;
                        string[] parts = Regex.Split(t, @"\s+");
                        if (parts.Length >= 2 && parts[1] == "device" && parts[0].Contains(":"))
                        {
                            connectedTarget = parts[0];
                            break;
                        }
                    }

                    SafeInvoke(delegate {
                        if (!string.IsNullOrEmpty(connectedTarget))
                        {
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "🟢 Wi-Fi Conectado: " + connectedTarget;
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                            }
                            txtIp.Text = connectedTarget;
                        }
                        else if (!string.IsNullOrEmpty(foundTarget))
                        {
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "🟡 Detectado mDNS: " + foundTarget;
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
                            }
                            // Só sobrescreve se o campo estiver vazio ou com o IP obsoleto 192.168.100.
                            string cur = txtIp.Text.Trim();
                            if (string.IsNullOrEmpty(cur) || cur.StartsWith("192.168.100."))
                            {
                                txtIp.Text = foundTarget;
                            }
                        }
                        else
                        {
                            if (txtWifiStatus != null)
                            {
                                txtWifiStatus.Text = "ℹ️ Dica: Clique em 'Auto-Detectar' ou 'Parear'.";
                                txtWifiStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                            }
                        }
                    });
                }
                catch { }
                finally
                {
                    isCheckingWifi = false;
                }
            });
        }

        private static void FixPort5555Async()
        {
            txtStatus.Text = "Fixando porta padrão 5555 no aparelho...";

            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    string targetDevice = null;
                    if (lastDetectedUsbState == "device" && !string.IsNullOrEmpty(lastDetectedUsbDevice))
                    {
                        targetDevice = "-s " + lastDetectedUsbDevice;
                    }
                    else
                    {
                        string devOut = RunCommand(adbPath, "devices", 2500);
                        string[] lines = devOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string l in lines)
                        {
                            string t = l.Trim();
                            if (t.StartsWith("List of") || string.IsNullOrEmpty(t)) continue;
                            string[] parts = Regex.Split(t, @"\s+");
                            if (parts.Length >= 2 && parts[1] == "device")
                            {
                                targetDevice = "-s " + parts[0];
                                break;
                            }
                        }

                        // Se não havia dispositivo ADB ativo, tenta descobrir via mDNS e conectar antes de fixar 5555
                        if (string.IsNullOrEmpty(targetDevice))
                        {
                            List<string> pfx = GetLocalSubnetPrefixes();
                            string mdnsTarget = DiscoverMdnsConnectEndpoint(pfx, null);
                            if (!string.IsNullOrEmpty(mdnsTarget))
                            {
                                RunCommand(adbPath, "connect " + mdnsTarget, 4000);
                                targetDevice = "-s " + mdnsTarget;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(targetDevice))
                    {
                        SafeInvoke(delegate {
                            MessageBox.Show("Nenhum aparelho Android ativo foi encontrado.\n\nPara fixar a porta 5555, conecte o celular via cabo USB uma vez ou estabeleça uma conexão prévia via Depuração Wi-Fi.", "Aparelho Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                            txtStatus.Text = "Conecte o celular para fixar a porta 5555.";
                        });
                        return;
                    }

                    RunCommand(adbPath, targetDevice + " tcpip 5555", 5000);

                    string serialClean = targetDevice.Replace("-s ", "").Trim();
                    string detectedIp = QueryDeviceWifiIp(serialClean);

                    if (string.IsNullOrEmpty(detectedIp))
                    {
                        string currentVal = "";
                        SafeInvoke(delegate { currentVal = txtIp.Text.Trim(); });
                        if (currentVal.Contains(":")) detectedIp = currentVal.Split(':')[0];
                        else if (!string.IsNullOrEmpty(currentVal)) detectedIp = currentVal;
                    }

                    SafeInvoke(delegate {
                        if (!string.IsNullOrEmpty(detectedIp))
                        {
                            string newTarget = detectedIp + ":5555";
                            txtIp.Text = newTarget;
                            try { File.WriteAllText(lastIpFile, newTarget); } catch { }
                            SaveSettings();
                            txtStatus.Text = "🟢 Modo TCP/IP fixado na porta 5555! Conectando...";
                            ConnectAdbWifiAsync();
                        }
                        else
                        {
                            txtStatus.Text = "Porta 5555 liberada! Digite o IP do Wi-Fi na caixa acima.";
                            MessageBox.Show("Porta 5555 habilitada com sucesso!\n\nInsira o IP do seu celular seguido de :5555 na caixa de texto.", "Porta 5555 Habilitada", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                catch { }
            });
        }

        private static void ShowPairingModal()
        {
            Window pairWin = new Window();
            pairWin.Title = "Aura SCRCPY - Pareamento Wi-Fi (Android 11+)";
            pairWin.Width = 520;
            pairWin.Height = 460;
            pairWin.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            pairWin.Owner = mainWindow;
            pairWin.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // #0F172A
            pairWin.Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            pairWin.ResizeMode = ResizeMode.NoResize;
            pairWin.WindowStyle = WindowStyle.ToolWindow;

            Grid rootGrid = new Grid();
            rootGrid.Margin = new Thickness(20);

            StackPanel sp = new StackPanel();

            TextBlock tbTitle = new TextBlock();
            tbTitle.Text = "🔑 Parear Aparelho via Wi-Fi (Android 11+)";
            tbTitle.FontSize = 16;
            tbTitle.FontWeight = FontWeights.Bold;
            tbTitle.Foreground = new SolidColorBrush(Color.FromRgb(192, 132, 252));
            tbTitle.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(tbTitle);

            Border infoBorder = new Border();
            infoBorder.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            infoBorder.CornerRadius = new CornerRadius(8);
            infoBorder.Padding = new Thickness(12);
            infoBorder.Margin = new Thickness(0, 0, 0, 14);

            TextBlock tbInstructions = new TextBlock();
            tbInstructions.Text = "1. No celular, abra Configurações > Opções do Desenvolvedor > Depuração por Wi-Fi.\n" +
                                  "2. Toque em 'Parear dispositivo com código de pareamento'.\n" +
                                  "3. Digite o IP com a Porta de Pareamento e o Código de 6 dígitos exibidos na tela.\n" +
                                  "⚠️ ATENÇÃO: A porta de pareamento é temporária e diferente da porta principal!";
            tbInstructions.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            tbInstructions.FontSize = 11.5;
            tbInstructions.TextWrapping = TextWrapping.Wrap;
            infoBorder.Child = tbInstructions;
            sp.Children.Add(infoBorder);

            TextBlock lblEndpoint = new TextBlock();
            lblEndpoint.Text = "Endereço IP e Porta de Pareamento (ex: 192.168.1.100:41235):";
            lblEndpoint.FontSize = 11.5;
            lblEndpoint.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            lblEndpoint.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(lblEndpoint);

            TextBox txtPairEndpoint = new TextBox();
            string prefill = "";
            string mdnsCheck = RunCommand(adbPath, "mdns services", 2500);
            Match mPair = Regex.Match(mdnsCheck, @"_adb-tls-pairing[^\r\n]*?([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}:[0-9]{2,5})");
            if (mPair.Success)
            {
                prefill = mPair.Groups[1].Value;
            }
            else
            {
                string cur = txtIp.Text.Trim();
                if (cur.Contains(":") && !cur.StartsWith("192.168.100.")) prefill = cur.Split(':')[0] + ":";
                else if (!string.IsNullOrEmpty(cur) && !cur.StartsWith("192.168.100.")) prefill = cur + ":";
                else prefill = "192.168.1.100:";
            }
            txtPairEndpoint.Text = prefill;
            txtPairEndpoint.Background = new SolidColorBrush(Color.FromRgb(2, 6, 23));
            txtPairEndpoint.Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            txtPairEndpoint.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            txtPairEndpoint.Padding = new Thickness(8, 6, 8, 6);
            txtPairEndpoint.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(txtPairEndpoint);

            TextBlock lblCode = new TextBlock();
            lblCode.Text = "Código de Pareamento Wi-Fi (6 dígitos):";
            lblCode.FontSize = 11.5;
            lblCode.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            lblCode.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(lblCode);

            TextBox txtPairCode = new TextBox();
            txtPairCode.Background = new SolidColorBrush(Color.FromRgb(2, 6, 23));
            txtPairCode.Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            txtPairCode.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            txtPairCode.Padding = new Thickness(8, 6, 8, 6);
            txtPairCode.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(txtPairCode);

            TextBlock txtPairStatus = new TextBlock();
            txtPairStatus.Text = "Aguardando confirmação do pareamento...";
            txtPairStatus.FontSize = 11;
            txtPairStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            txtPairStatus.Margin = new Thickness(0, 0, 0, 14);
            txtPairStatus.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(txtPairStatus);

            StackPanel btnPanel = new StackPanel();
            btnPanel.Orientation = Orientation.Horizontal;
            btnPanel.HorizontalAlignment = HorizontalAlignment.Right;

            Button btnPairSubmit = new Button();
            btnPairSubmit.Content = "🟢 Parear Dispositivo";
            btnPairSubmit.Padding = new Thickness(14, 8, 14, 8);
            btnPairSubmit.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            btnPairSubmit.Foreground = new SolidColorBrush(Color.FromRgb(167, 243, 208));
            btnPairSubmit.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            btnPairSubmit.BorderThickness = new Thickness(1);
            btnPairSubmit.FontWeight = FontWeights.SemiBold;
            btnPairSubmit.Cursor = Cursors.Hand;
            btnPairSubmit.Margin = new Thickness(0, 0, 8, 0);

            Button btnPairClose = new Button();
            btnPairClose.Content = "Fechar";
            btnPairClose.Padding = new Thickness(14, 8, 14, 8);
            btnPairClose.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            btnPairClose.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            btnPairClose.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            btnPairClose.BorderThickness = new Thickness(1);
            btnPairClose.Cursor = Cursors.Hand;

            btnPanel.Children.Add(btnPairSubmit);
            btnPanel.Children.Add(btnPairClose);
            sp.Children.Add(btnPanel);

            rootGrid.Children.Add(sp);
            pairWin.Content = rootGrid;

            btnPairClose.Click += delegate { pairWin.Close(); };

            btnPairSubmit.Click += delegate {
                string ep = txtPairEndpoint.Text.Trim();
                string code = txtPairCode.Text.Trim();

                if (string.IsNullOrEmpty(ep) || string.IsNullOrEmpty(code))
                {
                    MessageBox.Show("Por favor, preencha o IP com a porta de pareamento e o código de 6 dígitos.", "Campos Obrigatórios", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                btnPairSubmit.IsEnabled = false;
                txtPairStatus.Text = "⏳ Pareando com " + ep + "... Não feche a tela do celular.";
                txtPairStatus.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));

                ThreadPool.QueueUserWorkItem(delegate {
                    try
                    {
                        string pairRes = RunCommand(adbPath, "pair " + ep + " " + code, 7000);
                        bool success = pairRes.IndexOf("Successfully paired", StringComparison.OrdinalIgnoreCase) >= 0;

                        Action pairUiAction = delegate {
                            btnPairSubmit.IsEnabled = true;
                            if (success)
                            {
                                txtPairStatus.Text = "🟢 Pareamento concluído com sucesso!";
                                txtPairStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                                MessageBox.Show("Dispositivo pareado com sucesso!\n\nO Aura SCRCPY irá agora localizar e conectar a porta de transmissão.", "Pareamento Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                                pairWin.Close();

                                ThreadPool.QueueUserWorkItem(delegate {
                                    try
                                    {
                                        Thread.Sleep(800);
                                        AutoDetectWifiAsync();
                                    }
                                    catch { }
                                });
                            }
                            else
                            {
                                txtPairStatus.Text = "❌ Falha: " + pairRes.Trim();
                                txtPairStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                                MessageBox.Show("Falha no pareamento:\n\n" + pairRes + "\n\n💡 Verifique se a tela do celular ainda exibe o código (ele expira) e se a porta digitada é exatamente a que aparece na janela de pareamento.", "Erro de Pareamento", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        };

                        if (pairWin != null && pairWin.Dispatcher != null && !pairWin.Dispatcher.HasShutdownStarted)
                        {
                            pairWin.Dispatcher.Invoke(pairUiAction);
                        }
                    }
                    catch { }
                });
            };

            pairWin.ShowDialog();
        }

        private static void TestPingAsync()
        {
            try
            {
                string raw = "";
                SafeInvoke(delegate {
                    if (txtIp != null) raw = txtIp.Text.Trim();
                });

                if (string.IsNullOrEmpty(raw))
                {
                    SafeInvoke(delegate {
                        if (txtPingResult != null)
                        {
                            txtPingResult.Text = "⚠️ Digite o endereço IP do celular acima para testar o ping.";
                            txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(251, 146, 60));
                        }
                    });
                    return;
                }

                if (isTestingPing) return;
                isTestingPing = true;

                string ipOnly = raw.Trim();
                if (ipOnly.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ipOnly = ipOnly.Substring(7);
                if (ipOnly.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ipOnly = ipOnly.Substring(8);
                if (ipOnly.Contains("/")) ipOnly = ipOnly.Split('/')[0];
                
                if (ipOnly.StartsWith("["))
                {
                    int endBracket = ipOnly.IndexOf("]");
                    if (endBracket > 0) ipOnly = ipOnly.Substring(1, endBracket - 1);
                }
                else if (ipOnly.Contains(":")) 
                {
                    ipOnly = ipOnly.Split(':')[0];
                }
                ipOnly = ipOnly.Trim();

                System.Net.IPAddress parsedIp;
                bool isIp = System.Net.IPAddress.TryParse(ipOnly, out parsedIp);
                bool isMdns = ipOnly.EndsWith("._tcp", StringComparison.OrdinalIgnoreCase);
                if (!isIp && !isMdns && ipOnly.ToLower() != "localhost")
                {
                    SafeInvoke(delegate {
                        if (btnTestPing != null) btnTestPing.IsEnabled = true;
                        if (txtPingResult != null)
                        {
                            txtPingResult.Text = "⚠️ Endereço inválido. Insira um IP válido (ex: 192.168.0.5).";
                            txtPingResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
                        }
                    });
                    isTestingPing = false;
                    return;
                }

                SafeInvoke(delegate {
                    if (btnTestPing != null) btnTestPing.IsEnabled = false;
                    if (txtPingResult != null)
                    {
                        txtPingResult.Text = "⏳ Medindo latência com " + ipOnly + "...";
                        txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
                    }
                });

                ThreadPool.QueueUserWorkItem(delegate {
                    long totalMs = 0;
                    int count = 0;
                    try
                    {
                        using (Ping p = new Ping())
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                try
                                {
                                    PingReply rep = p.Send(ipOnly, 800);
                                    if (rep != null && rep.Status == IPStatus.Success)
                                    {
                                        totalMs += rep.RoundtripTime;
                                        count++;
                                    }
                                }
                                catch { }
                                Thread.Sleep(40);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        isTestingPing = false;
                    }

                    SafeInvoke(delegate {
                        if (btnTestPing != null) btnTestPing.IsEnabled = true;
                        if (txtPingResult == null) return;

                        if (count > 0)
                        {
                            long avg = totalMs / count;
                            if (avg < 20)
                            {
                                txtPingResult.Text = "🟢 Ping: " + avg + " ms (Excelente - Resposta imediata, fluidez 90 FPS)";
                                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                            }
                            else if (avg <= 45)
                            {
                                txtPingResult.Text = "🟡 Ping: " + avg + " ms (Bom - Fluidez estável para produtividade e vídeos)";
                                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
                            }
                            else
                            {
                                txtPingResult.Text = "🔴 Ping: " + avg + " ms (Alto delay - Recomendado usar Cabo USB ou rede 5GHz)";
                                txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                            }
                        }
                        else
                        {
                            txtPingResult.Text = "⚠️ Ping sem resposta ICMP (normal em alguns roteadores). Tente o botão 'Conectar ADB'.";
                            txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(251, 146, 60));
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                isTestingPing = false;
                SafeInvoke(delegate {
                    if (btnTestPing != null) btnTestPing.IsEnabled = true;
                    if (txtPingResult != null)
                    {
                        txtPingResult.Text = "⚠️ Falha ao medir ping: " + ex.Message;
                        txtPingResult.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    }
                });
            }
        }

        private static void LaunchScrcpy()
        {
            SaveSettings();
            bool isUsb = rbUsb.IsChecked == true;
            string targetIp = txtIp.Text.Trim();

            // Pre-validation
            if (isUsb && lastDetectedUsbState == "unauthorized")
            {
                MessageBox.Show("O aparelho USB detectado NÃO está autorizado.\n\nDesbloqueie a tela do celular e toque em 'Permitir a depuração USB' antes de iniciar.", "Aparelho Não Autorizado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!isUsb && string.IsNullOrEmpty(targetIp))
            {
                MessageBox.Show("Por favor, digite o IP do celular na aba Wi-Fi antes de iniciar.", "IP Vazio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnLaunch.IsEnabled = false;
            txtStatus.Text = "Preparando e iniciando SCRCPY...";

            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    string exactSerial = null;

                    if (isUsb)
                    {
                        exactSerial = !string.IsNullOrEmpty(lastDetectedUsbDevice) ? lastDetectedUsbDevice : null;
                    }
                    else
                    {
                        string fullTarget = targetIp;
                        if (!fullTarget.Contains(":"))
                        {
                            SafeInvoke(delegate { txtStatus.Text = "Tentando conexão na porta padrão 5555..."; });
                            string c1 = RunCommand(adbPath, "connect " + fullTarget + ":5555", 3000);
                            if (c1.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || c1.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                fullTarget = fullTarget + ":5555";
                            }
                            else
                            {
                                SafeInvoke(delegate { txtStatus.Text = "Buscando porta dinâmica via mDNS..."; });
                                List<string> pfx = GetLocalSubnetPrefixes();
                                string mdnsTarget = DiscoverMdnsConnectEndpoint(pfx, fullTarget);
                                if (!string.IsNullOrEmpty(mdnsTarget)) fullTarget = mdnsTarget;
                                else fullTarget = fullTarget + ":5555";
                            }
                        }

                        SafeInvoke(delegate { txtStatus.Text = "Conectando ao dispositivo: " + fullTarget + "..."; });
                        string connOut = RunCommand(adbPath, "connect " + fullTarget, 5000);
                        SafeInvoke(delegate { txtStatus.Text = "Validando status do dispositivo..."; });
                        string devCheck = RunCommand(adbPath, "devices -l", 2500);

                        string ipOnlyCheck = fullTarget.Contains(":") ? fullTarget.Split(':')[0] : fullTarget;
                        string[] devLines = devCheck.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string l in devLines)
                        {
                            string t = l.Trim();
                            if (t.StartsWith("List of") || string.IsNullOrEmpty(t)) continue;
                            string[] parts = Regex.Split(t, @"\s+");
                            if (parts.Length >= 2 && parts[1] == "device")
                            {
                                string s = parts[0];
                                if (s == fullTarget)
                                {
                                    exactSerial = s;
                                    break;
                                }
                                else if (exactSerial == null && s.StartsWith(ipOnlyCheck + ":"))
                                {
                                    exactSerial = s;
                                }
                                else if (exactSerial == null && s.Contains("._tcp"))
                                {
                                    exactSerial = s;
                                }
                            }
                        }

                        if (exactSerial == null)
                        {
                            // Fallback 1: Se falhou conectar na porta anterior, tenta descobrir porta mDNS dinâmica
                            List<string> pfx = GetLocalSubnetPrefixes();
                            string mdnsTarget = DiscoverMdnsConnectEndpoint(pfx, ipOnlyCheck);
                            if (!string.IsNullOrEmpty(mdnsTarget) && mdnsTarget != fullTarget)
                            {
                                string mConn = RunCommand(adbPath, "connect " + mdnsTarget, 4000);
                                if (mConn.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || mConn.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    fullTarget = mdnsTarget;
                                    exactSerial = mdnsTarget;
                                    SafeInvoke(delegate {
                                        txtIp.Text = mdnsTarget;
                                        try { File.WriteAllText(lastIpFile, mdnsTarget); } catch { }
                                        SaveSettings();
                                    });
                                }
                            }

                            // Fallback 2: Se USB estiver conectado, ativa tcpip 5555
                            if (exactSerial == null && lastDetectedUsbState == "device" && !string.IsNullOrEmpty(lastDetectedUsbDevice))
                            {
                                RunCommand(adbPath, "-s " + lastDetectedUsbDevice + " tcpip 5555", 3500);
                                string t5555Fallback = ipOnlyCheck + ":5555";
                                string uConn = RunCommand(adbPath, "connect " + t5555Fallback, 3500);
                                if (uConn.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || uConn.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    fullTarget = t5555Fallback;
                                    exactSerial = t5555Fallback;
                                    SafeInvoke(delegate {
                                        txtIp.Text = t5555Fallback;
                                        try { File.WriteAllText(lastIpFile, t5555Fallback); } catch { }
                                        SaveSettings();
                                    });
                                }
                            }
                        }

                        if (exactSerial == null)
                        {
                            bool hasTarget = devCheck.Contains(fullTarget) || 
                                             connOut.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                             connOut.IndexOf("already connected", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (hasTarget)
                            {
                                exactSerial = fullTarget;
                            }
                            else
                            {
                                SafeInvoke(delegate {
                                    txtStatus.Text = "❌ Não foi possível conectar ao Wi-Fi (" + fullTarget + ").";
                                    MessageBox.Show("Não foi possível conectar ao aparelho no endereço:\n" + fullTarget + "\n\nResposta do ADB:\n" + connOut.Trim() + "\n\n💡 Sugestões:\n1. Certifique-se de que a 'Depuração por Wi-Fi' está ATIVA na tela do celular.\n2. Clique em '🔍 Auto-Detectar' para capturar a porta dinâmica atual.\n3. Se for a primeira conexão sem cabo, use '🔑 Parear (Android 11+)'.\n4. Se o cabo USB estiver conectado, clique em '📲 Ativar Wi-Fi (TCP/IP)'.", "Falha de Conexão Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Warning);
                                });
                                return;
                            }
                        }
                    }

                    string selector = exactSerial != null ? "-s " + exactSerial : (isUsb ? "-d" : "-s " + targetIp);
                    string args = "";
                    bool hideVirtualKeyboard = false;
                    SafeInvoke(delegate {
                        args = BuildArgumentsForTarget(selector, isUsb);
                        hideVirtualKeyboard = chkNoVirtualKeyboard.IsChecked == true && chkCamera.IsChecked != true;
                    });

                    // Wake device up & keep stay awake
                    RunCommand(adbPath, selector + " shell input keyevent 224", 2000);
                    RunCommand(adbPath, selector + " shell wm dismiss-keyguard", 2000);
                    RunCommand(adbPath, selector + " shell svc power stayon true", 2000);

                    if (hideVirtualKeyboard)
                    {
                        RunCommand(adbPath, selector + " shell settings put secure show_ime_with_hard_keyboard 0", 2000);
                    }

                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = scrcpyPath;
                        psi.Arguments = args;
                        psi.WorkingDirectory = baseDir;
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardError = true;

                        Process proc = Process.Start(psi);
                        currentScrcpyProcess = proc;

                        proc.EnableRaisingEvents = true;
                        proc.Exited += delegate {
                            if (hideVirtualKeyboard && !string.IsNullOrEmpty(selector))
                            {
                                try { RunCommand(adbPath, selector + " shell settings put secure show_ime_with_hard_keyboard 1", 2000); } catch { }
                            }
                        };
                        
                        // Update UI toggle button to match initial state
                        SafeInvoke(delegate {
                            isScrcpyHidden = chkInvisibleMode.IsChecked == true;
                            if (isScrcpyHidden)
                            {
                                txtToggleVisibilityIcon.Text = "👁️";
                                txtToggleVisibilityText.Text = "Mostrar";
                            }
                            else
                            {
                                txtToggleVisibilityIcon.Text = "👻";
                                txtToggleVisibilityText.Text = "Ocultar";
                            }
                        });

                        StringBuilder errSb = new StringBuilder();
                        proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                            try {
                                if (!string.IsNullOrEmpty(e.Data)) errSb.AppendLine(e.Data);
                            } catch { }
                        };
                        proc.BeginErrorReadLine();

                        // Watchdog: monitor for 1500ms to catch startup crash
                        bool exited = proc.WaitForExit(1500);
                        int exitCode = exited ? proc.ExitCode : 0;
                        string errOutput = errSb.ToString();

                        if (exited && exitCode != 0)
                        {
                            SafeInvoke(delegate {
                                txtStatus.Text = "❌ SCRCPY encerrou com aviso.";
                                string tip = "Verifique as configurações selecionadas.";

                                if (errOutput.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
                                    tip = "O dispositivo não está autorizado. Toque em 'Permitir depuração USB' na tela do celular.";
                                else if (errOutput.IndexOf("device not found", StringComparison.OrdinalIgnoreCase) >= 0 || errOutput.IndexOf("Could not find any ADB device", StringComparison.OrdinalIgnoreCase) >= 0)
                                    tip = "Nenhum dispositivo encontrado. Verifique a conexão do cabo USB ou se o IP do Wi-Fi está correto.";
                                else if (errOutput.IndexOf("codec", StringComparison.OrdinalIgnoreCase) >= 0 || errOutput.IndexOf("encoder", StringComparison.OrdinalIgnoreCase) >= 0)
                                    tip = "O codec de vídeo escolhido não é suportado pelo seu aparelho. Mude o Codec para H.264 nos Ajustes Finos.";
                                else if (errOutput.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0)
                                    tip = "Falha no áudio. Desmarque 'Transmitir Áudio' (requer Android 11 ou superior).";

                                MessageBox.Show("O SCRCPY encerrou prematuramente:\n\n" + (string.IsNullOrEmpty(errOutput) ? "Código de saída: " + exitCode : errOutput.Trim()) + "\n\n💡 Sugestão: " + tip, "Alerta do SCRCPY", MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                        }
                        else
                        {
                            SafeInvoke(delegate {
                                txtStatus.Text = "🟢 Espelhamento ativo em execução! (PID: " + proc.Id + ")";
                                if (chkCloseOnLaunch.IsChecked == true)
                                {
                                    mainWindow.Close();
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        SafeInvoke(delegate {
                            MessageBox.Show("Erro ao executar scrcpy.exe:\n" + ex.Message, "Erro de Execução", MessageBoxButton.OK, MessageBoxImage.Error);
                            txtStatus.Text = "Falha ao iniciar scrcpy.exe.";
                        });
                    }
                }
                catch (Exception threadEx)
                {
                    SafeInvoke(delegate {
                        txtStatus.Text = "❌ Falha crítica: " + threadEx.Message;
                        MessageBox.Show("Ocorreu um erro fatal na thread de background:\n" + threadEx.ToString(), "Erro Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    SafeInvoke(delegate {
                        if (btnLaunch != null) btnLaunch.IsEnabled = true;
                    });
                }
            });
        }

        private static void StopScrcpy()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("scrcpy");
                int count = procs.Length;
                foreach (Process p in procs)
                {
                    try { p.Kill(); } catch { }
                }

                if (count > 0)
                {
                    txtStatus.Text = "⏹️ " + count + " processo(s) do SCRCPY encerrado(s).";
                }
                else
                {
                    txtStatus.Text = "Nenhum processo do SCRCPY estava em execução.";
                }

                bool hideVirtualKeyboard = false;
                SafeInvoke(delegate {
                    if (chkNoVirtualKeyboard != null) hideVirtualKeyboard = chkNoVirtualKeyboard.IsChecked == true;
                });
                if (hideVirtualKeyboard)
                {
                    ThreadPool.QueueUserWorkItem(delegate {
                        try { RunCommand(adbPath, "shell settings put secure show_ime_with_hard_keyboard 1", 2000); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Erro ao encerrar: " + ex.Message;
            }
        }

        private static void ToggleScrcpyVisibility()
        {
            try
            {
                if (currentScrcpyProcess == null || currentScrcpyProcess.HasExited)
                {
                    // Fallback to finding any running scrcpy process
                    Process[] procs = Process.GetProcessesByName("scrcpy");
                    if (procs.Length > 0)
                    {
                        currentScrcpyProcess = procs[0];
                    }
                    else
                    {
                        MessageBox.Show("O processo do SCRCPY não está em execução.", "SCRCPY Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                IntPtr handle = currentScrcpyProcess.MainWindowHandle;
                
                // If main window handle is not found immediately, we can wait a bit or try to find it
                if (handle == IntPtr.Zero)
                {
                    currentScrcpyProcess.Refresh();
                    handle = currentScrcpyProcess.MainWindowHandle;
                }

                if (handle != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetWindowRect(handle, out rect))
                    {
                        isScrcpyHidden = rect.Left < -20000;
                    }

                    if (isScrcpyHidden)
                    {
                        // Bring it back to visible coordinates (e.g. 120, 120)
                        SetWindowPos(handle, IntPtr.Zero, 120, 120, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
                        isScrcpyHidden = false;
                        
                        txtToggleVisibilityIcon.Text = "👻";
                        txtToggleVisibilityText.Text = "Ocultar";
                        txtStatus.Text = "👁️ Janela do SCRCPY restaurada para a tela.";
                    }
                    else
                    {
                        // Move it offscreen (-32000, -32000)
                        SetWindowPos(handle, IntPtr.Zero, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
                        isScrcpyHidden = true;

                        txtToggleVisibilityIcon.Text = "👁️";
                        txtToggleVisibilityText.Text = "Mostrar";
                        txtStatus.Text = "👻 Janela do SCRCPY oculta da área de trabalho.";
                    }
                }
                else
                {
                    MessageBox.Show("Não foi possível obter a janela do SCRCPY. Tente iniciar novamente.", "Janela Não Encontrada", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Erro ao alternar visibilidade: " + ex.Message;
            }
        }

        private static void LoadSettings()
        {
            txtIp.Text = "192.168.1.100:5555";

            if (File.Exists(lastIpFile))
            {
                try
                {
                    string ip = File.ReadAllText(lastIpFile).Trim();
                    if (!string.IsNullOrEmpty(ip) && !ip.StartsWith("192.168.100.")) txtIp.Text = ip;
                }
                catch { }
            }

            if (File.Exists(configFile))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configFile);
                    string savedPreset = null;

                    foreach (string l in lines)
                    {
                        string[] parts = l.Split('=');
                        if (parts.Length == 2)
                        {
                            string k = parts[0].Trim();
                            string v = parts[1].Trim();

                            if (k == "Mode" && v == "Wifi") rbWifi.IsChecked = true;
                            if (k == "Ip" && !string.IsNullOrEmpty(v) && !v.StartsWith("192.168.100.")) txtIp.Text = v;
                            if (k == "Preset") savedPreset = v;

                            int idx;
                            if (k == "Resolution" && int.TryParse(v, out idx)) cmbResolution.SelectedIndex = idx;
                            if (k == "Fps" && int.TryParse(v, out idx)) cmbFps.SelectedIndex = idx;
                            if (k == "Bitrate" && int.TryParse(v, out idx)) cmbBitrate.SelectedIndex = idx;
                            if (k == "Codec" && int.TryParse(v, out idx)) cmbCodec.SelectedIndex = idx;
                            if (k == "Buffer" && int.TryParse(v, out idx)) cmbBuffer.SelectedIndex = idx;
                            if (k == "CameraFacing" && int.TryParse(v, out idx)) cmbCameraFacing.SelectedIndex = idx;
                            if (k == "CameraOrientation" && int.TryParse(v, out idx)) cmbCameraOrientation.SelectedIndex = idx;

                            if (k == "ScreenOff") chkScreenOff.IsChecked = (v == "1");
                            if (k == "StayAwake") chkStayAwake.IsChecked = (v == "1");
                            if (k == "Audio") chkAudio.IsChecked = (v == "1");
                            if (k == "AlwaysOnTop") chkAlwaysOnTop.IsChecked = (v == "1");
                            if (k == "Borderless") chkBorderless.IsChecked = (v == "1");
                            if (k == "Fullscreen") chkFullscreen.IsChecked = (v == "1");
                            if (k == "ShowTouches") chkShowTouches.IsChecked = (v == "1");
                            if (k == "Record") chkRecord.IsChecked = (v == "1");
                            if (k == "Camera") chkCamera.IsChecked = (v == "1");
                            if (k == "Otg") chkOtg.IsChecked = (v == "1");
                            if (k == "CloseOnLaunch") chkCloseOnLaunch.IsChecked = (v == "1");
                            if (k == "InvisibleMode") chkInvisibleMode.IsChecked = (v == "1");
                            if (k == "NoVirtualKeyboard") chkNoVirtualKeyboard.IsChecked = (v == "1");
                        }
                    }

                    if (!string.IsNullOrEmpty(savedPreset))
                    {
                        if (savedPreset == "Ultra" || savedPreset == "Gaming" || savedPreset == "Balanced" || savedPreset == "2K" || savedPreset == "Eco" || savedPreset == "WebcamPro")
                        {
                            ApplyPreset(savedPreset);
                        }
                        else
                        {
                            MarkCustomPreset();
                        }
                    }
                }
                catch { }
            }
            else
            {
                ApplyPreset("Ultra");
            }
        }

        private static void SaveSettings()
        {
            try
            {
                if (!string.IsNullOrEmpty(txtIp.Text))
                {
                    File.WriteAllText(lastIpFile, txtIp.Text.Trim());
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Mode=" + (rbUsb.IsChecked == true ? "Usb" : "Wifi"));
                sb.AppendLine("Ip=" + txtIp.Text.Trim());
                sb.AppendLine("Preset=" + currentPresetName);
                sb.AppendLine("Resolution=" + cmbResolution.SelectedIndex);
                sb.AppendLine("Fps=" + cmbFps.SelectedIndex);
                sb.AppendLine("Bitrate=" + cmbBitrate.SelectedIndex);
                sb.AppendLine("Codec=" + cmbCodec.SelectedIndex);
                sb.AppendLine("Buffer=" + cmbBuffer.SelectedIndex);
                sb.AppendLine("CameraFacing=" + cmbCameraFacing.SelectedIndex);
                sb.AppendLine("CameraOrientation=" + cmbCameraOrientation.SelectedIndex);
                sb.AppendLine("ScreenOff=" + (chkScreenOff.IsChecked == true ? "1" : "0"));
                sb.AppendLine("StayAwake=" + (chkStayAwake.IsChecked == true ? "1" : "0"));
                sb.AppendLine("Audio=" + (chkAudio.IsChecked == true ? "1" : "0"));
                sb.AppendLine("AlwaysOnTop=" + (chkAlwaysOnTop.IsChecked == true ? "1" : "0"));
                sb.AppendLine("Borderless=" + (chkBorderless.IsChecked == true ? "1" : "0"));
                sb.AppendLine("Fullscreen=" + (chkFullscreen.IsChecked == true ? "1" : "0"));
                sb.AppendLine("ShowTouches=" + (chkShowTouches.IsChecked == true ? "1" : "0"));
                sb.AppendLine("Record=" + (chkRecord.IsChecked == true ? "1" : "0"));
                sb.AppendLine("Camera=" + (chkCamera.IsChecked == true ? "1" : "0"));
                sb.AppendLine("Otg=" + (chkOtg.IsChecked == true ? "1" : "0"));
                sb.AppendLine("CloseOnLaunch=" + (chkCloseOnLaunch.IsChecked == true ? "1" : "0"));
                sb.AppendLine("InvisibleMode=" + (chkInvisibleMode.IsChecked == true ? "1" : "0"));
                sb.AppendLine("NoVirtualKeyboard=" + (chkNoVirtualKeyboard.IsChecked == true ? "1" : "0"));

                File.WriteAllText(configFile, sb.ToString());
            }
            catch { }
        }

        private static string RunCommand(string file, string args, int timeoutMs = 4000)
        {
            try
            {
                if (!File.Exists(file))
                {
                    return "ERROR: Executável não encontrado: " + file;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = args;
                psi.WorkingDirectory = baseDir;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (Process proc = new Process())
                {
                    proc.StartInfo = psi;
                    proc.Start();

                    string stdOut = "";
                    string stdErr = "";

                    Thread outThread = new Thread(delegate() {
                        try { stdOut = proc.StandardOutput.ReadToEnd(); } catch { }
                    });
                    Thread errThread = new Thread(delegate() {
                        try { stdErr = proc.StandardError.ReadToEnd(); } catch { }
                    });

                    outThread.IsBackground = true;
                    errThread.IsBackground = true;
                    outThread.Start();
                    errThread.Start();

                    bool exited = proc.WaitForExit(timeoutMs);
                    if (exited)
                    {
                        outThread.Join(500);
                        errThread.Join(500);
                        string res = stdOut != null ? stdOut : "";
                        if (string.IsNullOrEmpty(res.Trim()) && !string.IsNullOrEmpty(stdErr))
                        {
                            res = stdErr;
                        }
                        return res;
                    }
                    else
                    {
                        try { proc.Kill(); } catch { }
                        outThread.Join(250);
                        errThread.Join(250);
                        string res = (stdOut ?? "") + "\n" + (stdErr ?? "");
                        return res.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }
    }
}
