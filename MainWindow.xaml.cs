using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace VirtualKeyboard
{
    public partial class MainWindow : Window
    {
        // 修饰键状态
        private bool isShiftPressed = false;
        private bool isCtrlPressed = false;
        private bool isAltPressed = false;
        private bool isCapsLockOn = false;

        private const double FontScaleMin = 0.75;
        private const double FontScaleMax = 1.60;
        private const double FontScaleStep = 0.10;
        private double fontScale = 1.00;
        private readonly Dictionary<Control, double> baseFontSizes = new Dictionary<Control, double>();

        // 存储修饰键按钮引用，用于更新视觉状态
        private Button shiftButton1 = null;
        private Button shiftButton2 = null;
        private Button ctrlButton = null;
        private Button altButton = null;
        private ToggleButton capsButton = null;
        
        // 通用长按重复机制
        private DispatcherTimer keyRepeatTimer = null;
        private DispatcherTimer keyDelayTimer = null;
        private bool isKeyRepeating = false;
        private Button currentRepeatButton = null;
        private Action<Button> repeatAction = null;
        
        // 长按配置
        private const int REPEAT_DELAY_MS = 500;  // 长按开始延迟
        private const int REPEAT_INTERVAL_MS = 20; // 重复间隔
        
        // 符号键映射：原字符 -> Shift后的字符
        private Dictionary<string, string> symbolShiftMap = new Dictionary<string, string>
        {
            { "`", "~" },
            { "-", "_" },
            { "=", "+" },
            { "[", "{" },
            { "]", "}" },
            { "\\", "|" },
            { ";", ":" },
            { "'", "\"" },
            { ",", "<" },
            { ".", ">" },
            { "/", "?" }
        };
        
        // 符号键反向映射：Shift后字符 -> 原字符（用于恢复）
        private Dictionary<string, string> symbolShiftReverseMap = null;
        
        // 初始化反向映射
        private void InitSymbolReverseMap()
        {
            if (symbolShiftReverseMap == null)
            {
                symbolShiftReverseMap = new Dictionary<string, string>();
                foreach (var kvp in symbolShiftMap)
                {
                    symbolShiftReverseMap[kvp.Value] = kvp.Key;
                }
            }
        }
        
        

        // ==================== Windows API 导入 ====================
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        // ==================== WM_COPYDATA 消息处理 ====================
        private const int WM_COPYDATA = 0x004A;

        // 命令定义
        private const int CMD_SHOW = 1;
        private const int CMD_HIDE = 2;
        private const int CMD_TOGGLE = 3;
        private const int CMD_SET_POSITION = 4;
        private const int CMD_GET_POSITION = 5;
        private const int CMD_MOVE_TO = 6;  // 相对移动

        // ==================== 窗口缩放相关 ====================
        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int RESIZE_MARGIN = 8;

        // COPYDATASTRUCT 结构
        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;    // 命令类型
            public int cbData;       // 数据长度
            public IntPtr lpData;    // 数据指针
        }

        // 位置数据结构
        [StructLayout(LayoutKind.Sequential)]
        private struct PositionData
        {
            public int X;
            public int Y;
        }

        // 移动数据结构（相对移动）
        [StructLayout(LayoutKind.Sequential)]
        private struct MoveData
        {
            public int DeltaX;
            public int DeltaY;
        }

        public MainWindow()
        {
            InitializeComponent();
            
            // 初始化符号键反向映射
            InitSymbolReverseMap();
            
            // 设置窗口图标
            SetWindowIcon();
            
            // 窗口加载后设置为不获取焦点
            this.SourceInitialized += Window_SourceInitialized;
            
            // 允许拖动窗口
            this.MouseLeftButtonDown += (s, e) => 
            {
                try
                {
                    this.DragMove();
                }
                catch { }
            };
            
            // 为所有按键添加点击事件
            this.Loaded += (s, e) =>
            {
                AttachKeyboardEvents(this);
                InitializeFontScaleToMax();
            };
        }

        // 设置窗口图标（代码生成）
        private void SetWindowIcon()
        {
            try
            {
                // 创建一个 32x32 的键盘图标
                int size = 32;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);

                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // 背景 - 深色
                    var bgColor = System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A);
                    var bgBrush = new System.Windows.Media.SolidColorBrush(bgColor);
                    dc.DrawRectangle(bgBrush, null, new Rect(0, 0, size, size));

                    // 键盘边框 - 绿色发光
                    var borderColor = System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x00);
                    var borderPen = new System.Windows.Media.Pen(
                        new System.Windows.Media.SolidColorBrush(borderColor), 2);
                    dc.DrawRectangle(null, borderPen, new Rect(1, 1, size - 2, size - 2));

                    // 键盘按键网格
                    var keyColor = System.Windows.Media.Color.FromRgb(0x3C, 0x3C, 0x3C);
                    var keyBrush = new System.Windows.Media.SolidColorBrush(keyColor);
                    var keyBorderPen = new System.Windows.Media.Pen(
                        new System.Windows.Media.SolidColorBrush(borderColor), 0.5);

                    // 第一行按键 (10个小键)
                    double keyWidth = 4;
                    double keyHeight = 3;
                    double startX = 3;
                    double startY = 4;
                    double gap = 1;

                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 10; col++)
                        {
                            double x = startX + col * (keyWidth + gap);
                            double y = startY + row * (keyHeight + gap);
                            dc.DrawRectangle(keyBrush, keyBorderPen, new Rect(x, y, keyWidth, keyHeight));
                        }
                    }

                    // 空格键
                    double spaceY = startY + 3 * (keyHeight + gap);
                    dc.DrawRectangle(keyBrush, keyBorderPen, new Rect(startX + 2 * (keyWidth + gap), spaceY, 6 * keyWidth + 5 * gap, keyHeight));
                }

                bitmap.Render(visual);
                this.Icon = bitmap;

                // 保存图标文件（仅在首次运行时）
                SaveIconFile(bitmap);
            }
            catch
            {
                // 如果生成失败，使用默认图标
            }
        }

        // 保存图标文件
        private void SaveIconFile(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keyboard.ico");
                
                // 如果已存在则跳过
                if (System.IO.File.Exists(iconPath)) return;

                // 转换为 Bitmap 并保存为 ICO
                using (var stream = new System.IO.MemoryStream())
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    encoder.Save(stream);

                    using (var bmp = new System.Drawing.Bitmap(stream))
                    {
                        // 使用 Icon.FromHandle 创建图标
                        IntPtr hIcon = bmp.GetHicon();
                        using (var icon = System.Drawing.Icon.FromHandle(hIcon))
                        {
                            using (var fileStream = new System.IO.FileStream(iconPath, System.IO.FileMode.Create))
                            {
                                icon.Save(fileStream);
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Icon saved to: {iconPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save icon: {ex.Message}");
            }
        }

        // 拖拽区域点击事件
        private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                this.DragMove();
            }
            catch { }
        }

        // 关闭按钮点击事件
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void FontSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string action)
            {
                return;
            }

            EnsureBaseFontSizes();

            if (action == "FontSmaller")
            {
                fontScale = Math.Max(FontScaleMin, Math.Round(fontScale - FontScaleStep, 2));
            }
            else if (action == "FontLarger")
            {
                fontScale = Math.Min(FontScaleMax, Math.Round(fontScale + FontScaleStep, 2));
            }

            ApplyFontScale();
        }

        private void InitializeFontScaleToMax()
        {
            EnsureBaseFontSizes();
            fontScale = FontScaleMax;
            ApplyFontScale();
        }

        // 主题切换按钮点击事件
        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string themeName)
            {
                return;
            }

            ApplyTheme(themeName);
        }
      
        // 应用配色主题（不改布局，只切换颜色资源）
        private void ApplyTheme(string themeName)
        {
            switch (themeName)
            {
                case "IceBlue":
                    SetThemeResources(
                        keyBackground: "#2A354A",
                        keyBorder: "#56C6FF",
                        keyHoverBackground: "#344A68",
                        keyPressedBackground: "#243449",
                        keyPressedBorder: "#3FAFE8",
                        modifierActiveBackground: "#21485C",
                        mainBackground: "#121A24",
                        dragBarBackground: "#1B2838",
                        titleForeground: "#56C6FF",
                        glowColor: "#56C6FF");
                    break;

                case "Purple":
                    SetThemeResources(
                        keyBackground: "#3A2F4A",
                        keyBorder: "#C084FC",
                        keyHoverBackground: "#493C5E",
                        keyPressedBackground: "#302742",
                        keyPressedBorder: "#A96CED",
                        modifierActiveBackground: "#5B2E79",
                        mainBackground: "#1B1524",
                        dragBarBackground: "#251C33",
                        titleForeground: "#C084FC",
                        glowColor: "#C084FC");
                    break;

                case "Amber":
                    SetThemeResources(
                        keyBackground: "#4A3824",
                        keyBorder: "#F59E0B",
                        keyHoverBackground: "#5A472F",
                        keyPressedBackground: "#3B2E1E",
                        keyPressedBorder: "#D68608",
                        modifierActiveBackground: "#6A4C1A",
                        mainBackground: "#22180E",
                        dragBarBackground: "#2E2214",
                        titleForeground: "#F59E0B",
                        glowColor: "#F59E0B");
                    break;

                case "ClassicBlack":
                    SetThemeResources(
                        keyBackground: "#1A1A1A",
                        keyBorder: "#E0E0E0",
                        keyHoverBackground: "#2A2A2A",
                        keyPressedBackground: "#000000",
                        keyPressedBorder: "#FFFFFF",
                        modifierActiveBackground: "#333333",
                        mainBackground: "#0D0D0D",
                        dragBarBackground: "#151515",
                        titleForeground: "#E0E0E0",
                        glowColor: "#444444");
                    break;

                case "ClassicWhite":
                    SetThemeResources(
                        keyBackground: "#F9F9F9",
                        keyBorder: "#333333",
                        keyHoverBackground: "#E5E5E5",
                        keyPressedBackground: "#D4D4D4",
                        keyPressedBorder: "#000000",
                        modifierActiveBackground: "#D0D0D0",
                        mainBackground: "#FFFFFF",
                        dragBarBackground: "#EFEFEF",
                        titleForeground: "#333333",
                        glowColor: "#AAAAAA");
                    break;

                case "NeonGreen":
                default:
                    SetThemeResources(
                        keyBackground: "#3C3C3C",
                        keyBorder: "#00FF00",
                        keyHoverBackground: "#4A4A4A",
                        keyPressedBackground: "#2A2A2A",
                        keyPressedBorder: "#00CC00",
                        modifierActiveBackground: "#1A5C1A",
                        mainBackground: "#1A1A1A",
                        dragBarBackground: "#2A2A2A",
                        titleForeground: "#00FF00",
                        glowColor: "#00FF00");
                    break;
            }

            RefreshModifierVisuals();
        }

        private void EnsureBaseFontSizes()
        {
            if (baseFontSizes.Count > 0)
            {
                return;
            }

            var controls = new List<Control>();
            CollectFontScalableControls(this, controls);
            foreach (var control in controls)
            {
                if (!baseFontSizes.ContainsKey(control))
                {
                    baseFontSizes[control] = control.FontSize;
                }
            }
        }

        private void ApplyFontScale()
        {
            foreach (var kvp in baseFontSizes)
            {
                kvp.Key.FontSize = kvp.Value * fontScale;
            }
        }

        private void CollectFontScalableControls(DependencyObject parent, List<Control> results)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is Control control && IsFontScalableControl(control))
                {
                    results.Add(control);
                }

                CollectFontScalableControls(child, results);
            }
        }

        private bool IsFontScalableControl(Control control)
        {
            if (control is Button button)
            {
                if (button == CloseButton)
                {
                    return false;
                }

                string tag = button.Tag?.ToString() ?? "";
                if (tag == "NeonGreen" || tag == "IceBlue" || tag == "Purple" || tag == "Amber" || tag == "ClassicBlack" || tag == "ClassicWhite")
                {
                    return false;
                }

                if (tag == "FontSmaller" || tag == "FontLarger")
                {
                    return false;
                }
            }

            return control is Button || control is ToggleButton;
        }

        private void SetThemeResources(
            string keyBackground,
            string keyBorder,
            string keyHoverBackground,
            string keyPressedBackground,
            string keyPressedBorder,
            string modifierActiveBackground,
            string mainBackground,
            string dragBarBackground,
            string titleForeground,
            string glowColor)
        {
            Resources["KeyBackgroundBrush"] = CreateBrush(keyBackground);
            Resources["KeyBorderBrush"] = CreateBrush(keyBorder);
            Resources["KeyForegroundBrush"] = CreateBrush(keyBorder);
            Resources["KeyHoverBackgroundBrush"] = CreateBrush(keyHoverBackground);
            Resources["KeyPressedBackgroundBrush"] = CreateBrush(keyPressedBackground);
            Resources["KeyPressedBorderBrush"] = CreateBrush(keyPressedBorder);
            Resources["ModifierActiveBackgroundBrush"] = CreateBrush(modifierActiveBackground);
            Resources["MainBackgroundBrush"] = CreateBrush(mainBackground);
            Resources["MainBorderBrush"] = CreateBrush(keyBorder);
            Resources["DragBarBackgroundBrush"] = CreateBrush(dragBarBackground);
            Resources["TitleForegroundBrush"] = CreateBrush(titleForeground);
            Resources["KeyGlowColor"] = (Color)ColorConverter.ConvertFromString(glowColor);
        }

        private static SolidColorBrush CreateBrush(string hexColor)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        }

        private SolidColorBrush GetThemeBrush(string resourceKey, Color fallbackColor)
        {
            if (TryFindResource(resourceKey) is SolidColorBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(fallbackColor);
        }

        private void RefreshModifierVisuals()
        {
            UpdateModifierKeyVisual(shiftButton1, isShiftPressed);
            UpdateModifierKeyVisual(shiftButton2, isShiftPressed);
            UpdateModifierKeyVisual(ctrlButton, isCtrlPressed);
            UpdateModifierKeyVisual(altButton, isAltPressed);
        }

        // 窗口初始化 - 设置缩放边框
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            
            // 设置窗口为不获取焦点、不显示在任务栏
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            
            // 使用 HwndSource.AddHook 处理所有消息
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProcHook);
        }

        // 窗口消息处理（统一处理 WM_COPYDATA 和 WM_NCHITTEST）
        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 处理 WM_COPYDATA 消息
            if (msg == WM_COPYDATA)
            {
                try
                {
                    HandleCopyDataMessage(lParam);
                    handled = true;
                    return IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WM_COPYDATA error: {ex.Message}");
                }
            }
            
            // 处理 WM_NCHITTEST 消息（窗口缩放）
            if (msg == WM_NCHITTEST)
            {
                // 获取鼠标位置（屏幕坐标）
                int x = lParam.ToInt32() & 0xFFFF;
                int y = lParam.ToInt32() >> 16;
                
                // 转换为窗口坐标
                var point = PointFromScreen(new Point(x, y));
                x = (int)point.X;
                y = (int)point.Y;
                
                double windowWidth = ActualWidth;
                double windowHeight = ActualHeight;
                
                // 判断鼠标在哪个边框区域
                bool onLeft = x <= RESIZE_MARGIN;
                bool onRight = x >= windowWidth - RESIZE_MARGIN;
                bool onTop = y <= RESIZE_MARGIN;
                bool onBottom = y >= windowHeight - RESIZE_MARGIN;
                
                if (onTop && onLeft)
                {
                    handled = true;
                    return (IntPtr)HTTOPLEFT;
                }
                else if (onTop && onRight)
                {
                    handled = true;
                    return (IntPtr)HTTOPRIGHT;
                }
                else if (onBottom && onLeft)
                {
                    handled = true;
                    return (IntPtr)HTBOTTOMLEFT;
                }
                else if (onBottom && onRight)
                {
                    handled = true;
                    return (IntPtr)HTBOTTOMRIGHT;
                }
                else if (onLeft)
                {
                    handled = true;
                    return (IntPtr)HTLEFT;
                }
                else if (onRight)
                {
                    handled = true;
                    return (IntPtr)HTRIGHT;
                }
                else if (onTop)
                {
                    handled = true;
                    return (IntPtr)HTTOP;
                }
                else if (onBottom)
                {
                    handled = true;
                    return (IntPtr)HTBOTTOM;
                }
            }
            
            return IntPtr.Zero;
        }

        // 处理 WM_COPYDATA 消息
        private void HandleCopyDataMessage(IntPtr lParam)
        {
            COPYDATASTRUCT cds = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
            int command = cds.dwData.ToInt32();

            this.Dispatcher.Invoke(() =>
            {
                switch (command)
                {
                    case CMD_SHOW:
                        this.Show();
                        System.Diagnostics.Debug.WriteLine("Keyboard: Show");
                        break;

                    case CMD_HIDE:
                        this.Hide();
                        System.Diagnostics.Debug.WriteLine("Keyboard: Hide");
                        break;

                    case CMD_TOGGLE:
                        if (this.IsVisible)
                            this.Hide();
                        else
                            this.Show();
                        System.Diagnostics.Debug.WriteLine("Keyboard: Toggle");
                        break;

                    case CMD_SET_POSITION:
                        if (cds.cbData >= Marshal.SizeOf(typeof(PositionData)))
                        {
                            PositionData pos = (PositionData)Marshal.PtrToStructure(cds.lpData, typeof(PositionData));
                            this.Left = pos.X;
                            this.Top = pos.Y;
                            System.Diagnostics.Debug.WriteLine($"Keyboard: SetPosition ({pos.X}, {pos.Y})");
                        }
                        break;

                    case CMD_MOVE_TO:
                        if (cds.cbData >= Marshal.SizeOf(typeof(MoveData)))
                        {
                            MoveData move = (MoveData)Marshal.PtrToStructure(cds.lpData, typeof(MoveData));
                            this.Left += move.DeltaX;
                            this.Top += move.DeltaY;
                            System.Diagnostics.Debug.WriteLine($"Keyboard: MoveTo ({move.DeltaX}, {move.DeltaY})");
                        }
                        break;

                    default:
                        System.Diagnostics.Debug.WriteLine($"Keyboard: Unknown command {command}");
                        break;
                }
            });
        }

        private void AttachKeyboardEvents(DependencyObject parent)
        {
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                
                if (child is Button button)
                {
                    button.Click += Button_Click;
                    
                    string content = button.Content?.ToString() ?? "";
                    
                    // 判断是否需要长按重复功能
                    bool needsRepeat = false;
                    
                    // Backspace键（宽度>60的←键）
                    if (content == "←" && button.Width > 60)
                    {
                        button.Tag = "Backspace";
                        needsRepeat = true;
                    }
                    // 字母键
                    else if (content.Length == 1 && char.IsLetter(content[0]))
                    {
                        button.Tag = "Letter";
                        needsRepeat = true;
                    }
                    // 数字键
                    else if (content.Length == 1 && char.IsDigit(content[0]))
                    {
                        button.Tag = "Number";
                        needsRepeat = true;
                    }
                    // 符号键
                    else if (symbolShiftMap.ContainsKey(content) || 
                             (symbolShiftReverseMap != null && symbolShiftReverseMap.ContainsKey(content)))
                    {
                        button.Tag = "Symbol";
                        needsRepeat = true;
                    }
                    
                    // 为需要长按重复的键添加事件
                    if (needsRepeat)
                    {
                        button.PreviewMouseLeftButtonDown += RepeatableKey_PreviewMouseLeftButtonDown;
                        button.PreviewMouseLeftButtonUp += RepeatableKey_PreviewMouseLeftButtonUp;
                        button.MouseLeave += RepeatableKey_MouseLeave;
                    }
                    
                    // 存储修饰键按钮引用
                    if (content == "Shift" && shiftButton1 == null)
                        shiftButton1 = button;
                    else if (content == "Shift" && shiftButton2 == null)
                        shiftButton2 = button;
                    else if (content == "Ctrl")
                        ctrlButton = button;
                    else if (content == "Alt")
                        altButton = button;
                    
                    // 初始化字母按钮为小写
                    if (content.Length == 1 && char.IsLetter(content[0]) && char.IsUpper(content[0]))
                    {
                        button.Content = content.ToLower();
                    }
                }
                else if (child is ToggleButton toggleButton)
                {
                    toggleButton.Click += ToggleButton_Click;
                    // 存储 CapsLock 按钮引用
                    if (toggleButton.Content?.ToString() == "CAP")
                        capsButton = toggleButton;
                }
                
                AttachKeyboardEvents(child);
            }
        }

        // 更新修饰键按钮的视觉状态
        private void UpdateModifierKeyVisual(Button button, bool isPressed)
        {
            if (button == null) return;
            
            if (isPressed)
            {
                button.Background = GetThemeBrush("ModifierActiveBackgroundBrush", Colors.DarkGreen);
                button.BorderBrush = GetThemeBrush("KeyBorderBrush", Colors.LimeGreen);
            }
            else
            {
                button.Background = GetThemeBrush("KeyBackgroundBrush", Colors.DimGray);
                button.BorderBrush = GetThemeBrush("KeyBorderBrush", Colors.LimeGreen);
            }
        }

        // 更新符号键显示（Shift激活时切换字符）
        private void UpdateSymbolKeysDisplay(DependencyObject parent, bool isShiftOn)
        {
            InitSymbolReverseMap();
            
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                
                if (child is Button button)
                {
                    string content = button.Content?.ToString() ?? "";
                    
                    // 检查是否是符号键（通过Tag标记）
                    if (button.Tag?.ToString() == "Symbol")
                    {
                        // 恢复原始字符后再切换
                        string originalContent = symbolShiftReverseMap.ContainsKey(content) ? symbolShiftReverseMap[content] : content;
                        button.Content = isShiftOn ? symbolShiftMap[originalContent] : originalContent;
                    }
                    // 检查是否是字母键（显示大写/小写）
                    else if (button.Tag?.ToString() == "Letter" && content.Length == 1 && char.IsLetter(content[0]))
                    {
                        // Shift激活时显示大写，否则使用CapsLock状态
                        button.Content = isShiftOn ? content.ToUpper() : (isCapsLockOn ? content.ToUpper() : content.ToLower());
                    }
                }
                
                UpdateSymbolKeysDisplay(child, isShiftOn);
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleButton)
            {
                string content = toggleButton.Content?.ToString() ?? "";
                
                if (content == "CAP")
                {
                    isCapsLockOn = toggleButton.IsChecked == true;
                    // 更新所有字母按钮的显示
                    UpdateSymbolKeysDisplay(this, isShiftPressed);
                }
            }
        }

        // 辅助方法：根据 CapsLock 状态发送字母
        private void SendLetter(byte virtualKey)
        {
            // isCapsLockOn = true 时发送大写（带 Shift）
            // isCapsLockOn = false 时发送小写（不带 Shift）
            KeyboardHelper.SendKey(virtualKey, isCapsLockOn);
        }

        // ==================== 通用长按重复机制 ====================
        
        // 可重复按键按下
        private void RepeatableKey_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Button button)
            {
                // 如果已有按键在重复，先停止
                if (currentRepeatButton != null && currentRepeatButton != button)
                {
                    StopKeyRepeat();
                }
                
                currentRepeatButton = button;
                
                // 立即发送一次
                SendKeyForButton(button);
                
                // 设置重复动作
                repeatAction = btn => SendKeyForButton(btn);
                
                // 启动延迟定时器
                if (keyDelayTimer == null)
                {
                    keyDelayTimer = new DispatcherTimer();
                    keyDelayTimer.Interval = TimeSpan.FromMilliseconds(REPEAT_DELAY_MS);
                    keyDelayTimer.Tick += (s, args) =>
                    {
                        keyDelayTimer.Stop();
                        isKeyRepeating = true;
                        StartKeyRepeat();
                    };
                }
                keyDelayTimer.Start();
            }
        }

        // 可重复按键释放
        private void RepeatableKey_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            StopKeyRepeat();
        }

        // 鼠标离开按键
        private void RepeatableKey_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Button button && button == currentRepeatButton)
            {
                StopKeyRepeat();
            }
        }

        // 开始重复
        private void StartKeyRepeat()
        {
            if (keyRepeatTimer == null)
            {
                keyRepeatTimer = new DispatcherTimer();
                keyRepeatTimer.Interval = TimeSpan.FromMilliseconds(REPEAT_INTERVAL_MS);
                keyRepeatTimer.Tick += (s, args) =>
                {
                    if (isKeyRepeating && currentRepeatButton != null && repeatAction != null)
                    {
                        repeatAction(currentRepeatButton);
                    }
                };
            }
            keyRepeatTimer.Start();
        }

        // 停止重复
        private void StopKeyRepeat()
        {
            isKeyRepeating = false;
            currentRepeatButton = null;
            repeatAction = null;
            
            if (keyDelayTimer != null)
                keyDelayTimer.Stop();
            if (keyRepeatTimer != null)
                keyRepeatTimer.Stop();
        }

        // 根据按钮发送对应的按键
        private void SendKeyForButton(Button button)
        {
            string content = button.Content?.ToString() ?? "";
            string tag = button.Tag?.ToString() ?? "";
            
            // Backspace
            if (tag == "Backspace")
            {
                KeyboardHelper.SendKey(KeyboardHelper.VK.BACK, isShiftPressed, isCtrlPressed, isAltPressed);
                return;
            }
            
            // 字母键
            if (tag == "Letter" && content.Length == 1 && char.IsLetter(content[0]))
            {
                char upperChar = char.ToUpper(content[0]);
                byte virtualKey = (byte)upperChar;
                bool needShift = isShiftPressed || isCapsLockOn;
                KeyboardHelper.SendKey(virtualKey, needShift, isCtrlPressed, isAltPressed);
                return;
            }
            
            // 数字键
            if (tag == "Number" && content.Length == 1 && char.IsDigit(content[0]))
            {
                byte virtualKey = (byte)(KeyboardHelper.VK.KEY_0 + (content[0] - '0'));
                KeyboardHelper.SendKey(virtualKey, isShiftPressed, isCtrlPressed, isAltPressed);
                return;
            }
            
            // 符号键
            if (tag == "Symbol")
            {
                SendSymbolKey(content);
                return;
            }
        }

        // 发送符号键
        private void SendSymbolKey(string content)
        {
            switch (content)
            {
                case "-":
                case "_":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_MINUS, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case "=":
                case "+":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_PLUS, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case "[":
                case "{":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_4, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case "]":
                case "}":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_6, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case ";":
                case ":":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_1, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case "'":
                case "\"":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_7, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case ",":
                case "<":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_COMMA, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case ".":
                case ">":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_PERIOD, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case "/":
                case "?":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_2, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case "`":
                case "~":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_3, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
                case "\\":
                case "|":
                    KeyboardHelper.SendKey(KeyboardHelper.VK.OEM_5, isShiftPressed, isCtrlPressed, isAltPressed);
                    break;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string content = button.Content?.ToString() ?? "";
                string tag = button.Tag?.ToString() ?? "";

                // 可重复按键（Backspace、字母、数字、符号）由 PreviewMouseLeftButtonDown/Up 处理
                // 这里跳过，避免重复发送
                if (tag == "Backspace" || tag == "Letter" || tag == "Number" || tag == "Symbol")
                {
                    return;
                }

                if (tag == "NeonGreen" || tag == "IceBlue" || tag == "Purple" || tag == "Amber" || tag == "ClassicBlack" || tag == "ClassicWhite")
                    return;
                if (tag == "FontSmaller" || tag == "FontLarger")
                    return;

                // 调试输出
                System.Diagnostics.Debug.WriteLine($"Button clicked: '{content}', Shift={isShiftPressed}, Ctrl={isCtrlPressed}, Alt={isAltPressed}");
                
                // 处理 Shift - 锁定模式（点击激活，再次点击释放），每次点击都发送Shift
                if (content == "Shift")
                {
                    // 切换状态
                    isShiftPressed = !isShiftPressed;
                    
                    // 每次点击都发送一次Shift
                    KeyboardHelper.SendKey(KeyboardHelper.VK.SHIFT);
                    
                    // 如果关闭Shift，发送释放事件
                    if (!isShiftPressed)
                        KeyboardHelper.SendKeyRelease(KeyboardHelper.VK.SHIFT);
                    
                    // 更新修饰键视觉状态
                    UpdateModifierKeyVisual(shiftButton1, isShiftPressed);
                    UpdateModifierKeyVisual(shiftButton2, isShiftPressed);
                    
                    // 更新符号键显示（切换字符）
                    UpdateSymbolKeysDisplay(this, isShiftPressed);
                    
                    return;
                }
                
                // 处理 Ctrl - 锁定模式（点击激活，再次点击释放）
                if (content == "Ctrl")
                {
                    isCtrlPressed = !isCtrlPressed;
                    UpdateModifierKeyVisual(ctrlButton, isCtrlPressed);
                    if (!isCtrlPressed)
                        KeyboardHelper.SendKeyRelease(KeyboardHelper.VK.CONTROL);
                    return;
                }
                
                // 处理 Alt - 锁定模式（点击激活，再次点击释放）
                if (content == "Alt")
                {
                    isAltPressed = !isAltPressed;
                    UpdateModifierKeyVisual(altButton, isAltPressed);
                    if (!isAltPressed)
                        KeyboardHelper.SendKeyRelease(KeyboardHelper.VK.MENU);
                    return;
                }
                
                // 处理特殊按键（使用当前修饰键状态）
                switch (content)
                {
                    case "Esc":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.ESCAPE, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    case "Tab":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.TAB, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    case "Enter":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.RETURN, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    case "↓":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.DOWN, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    case "↑":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.UP, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    case "←":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.LEFT, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    case "→":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.RIGHT, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    case "":
                    case " ":
                    case "Space":
                        KeyboardHelper.SendKey(KeyboardHelper.VK.SPACE, isShiftPressed, isCtrlPressed, isAltPressed);
                        break;
                    
                    default:
                        // 如果是其他单个字符，直接发送
                        if (content.Length == 1)
                        {
                            KeyboardHelper.SendChar(content[0], isCtrlPressed, isAltPressed);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Unhandled button: '{content}'");
                        }
                        break;
                }
                
                // Shift/Ctrl/Alt 都是锁定模式，按其他键后保持激活状态
            }
        }
    }
}
