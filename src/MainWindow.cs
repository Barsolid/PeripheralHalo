// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PeripheralHalo
{
    public sealed class MainWindow : Window
    {
        private readonly HaloApp app;
        private Grid root;
        private StackPanel body;
        private TextBlock footer,subtitle;
        private ScrollViewer scroll;
        private bool settingsOpen;
        private bool dismiss=true;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle,int attr,ref int value,int size);
        public MainWindow(HaloApp host)
        {
            app=host;Title="PeripheralHalo · 外设光环";Width=468;Height=790;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=host.Demo;Topmost=true;Background=Palette.Brush("#101624");Foreground=Palette.Text;
            FontFamily=new FontFamily("Segoe UI Variable, Microsoft YaHei UI, Segoe UI");FontSize=13;UseLayoutRounding=true;SnapsToDevicePixels=true;
            Resources.Add(typeof(Button),ButtonStyle());Resources.Add(typeof(CheckBox),CheckStyle());
            Resources.Add(typeof(ScrollBar),System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ScrollBar'><Setter Property='Width' Value='6'/><Setter Property='Margin' Value='5,0,0,0'/><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track Name='PART_Track' IsDirectionReversed='True' Orientation='Vertical'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#46536E' CornerRadius='3' Margin='1,2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style>"));
            BuildShell();
            SourceInitialized+=delegate {var h=new WindowInteropHelper(this).Handle;int dark=1,round=2;DwmSetWindowAttribute(h,20,ref dark,4);DwmSetWindowAttribute(h,33,ref round,4);};
            Deactivated+=delegate {if(dismiss && !app.Demo) Hide();};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape) Hide();};
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) {if(!app.Exiting) {e.Cancel=true;Hide();}};
        }
        private static Style ButtonStyle()
        {
            var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.ForegroundProperty,Palette.Text));style.Setters.Add(new Setter(Control.BackgroundProperty,Palette.Brush("#252D43")));style.Setters.Add(new Setter(Control.BorderBrushProperty,Palette.Line));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,8,12,8)));style.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));
            var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
            var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(Button)) {VisualTree=border}));
            var hover=new Trigger {Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,Palette.Brush("#35415E")));style.Triggers.Add(hover);
            var focus=new Trigger {Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Control.BorderBrushProperty,Palette.Accent));style.Triggers.Add(focus);return style;
        }
        private static Style CheckStyle()
        {
            var style=new Style(typeof(CheckBox));style.Setters.Add(new Setter(Control.ForegroundProperty,Palette.Muted));style.Setters.Add(new Setter(Control.FontSizeProperty,12.0));style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));style.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));return style;
        }
        private static TextBlock Text(string text,double size,Brush color)
        {return new TextBlock {Text=text,FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};}
        private static Button Button(string title,RoutedEventHandler action)
        {var button=new Button {Content=title};button.Click+=action;System.Windows.Automation.AutomationProperties.SetName(button,title);return button;}
        private static Border Pill(string label,Brush color)
        {return new Border {CornerRadius=new CornerRadius(6),Background=Palette.Brush("#242D42"),Padding=new Thickness(9,4,9,4),Child=Text(label,10,color),VerticalAlignment=VerticalAlignment.Center};}
        private void BuildShell()
        {
            root=new Grid {Margin=new Thickness(24,22,24,16)};root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});Content=root;
            var header=new Grid {Margin=new Thickness(0,0,0,18)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var brand=new StackPanel();var name=Text("PeripheralHalo",23,Palette.Text);name.FontWeight=FontWeights.SemiBold;brand.Children.Add(name);subtitle=Text("外设光环  /  电量，一眼可见",11,Palette.Muted);subtitle.Margin=new Thickness(0,5,0,0);brand.Children.Add(subtitle);header.Children.Add(brand);
            var actions=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};var settings=Button("设置",delegate {settingsOpen=!settingsOpen;Render();});settings.Padding=new Thickness(10,6,10,6);var close=Button("×",delegate {Hide();});close.Padding=new Thickness(11,4,11,4);close.FontSize=18;close.Margin=new Thickness(6,0,0,0);actions.Children.Add(settings);actions.Children.Add(close);Grid.SetColumn(actions,1);header.Children.Add(actions);root.Children.Add(header);
            body=new StackPanel();scroll=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,2,0)};Grid.SetRow(scroll,1);root.Children.Add(scroll);
            var bottom=new Grid {Margin=new Thickness(0,14,0,0)};bottom.ColumnDefinitions.Add(new ColumnDefinition());bottom.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});footer=Text("正在扫描设备…",10,Palette.Muted);bottom.Children.Add(footer);var refresh=Button("刷新",async delegate {await app.Refresh(true);});refresh.Padding=new Thickness(12,6,12,6);Grid.SetColumn(refresh,1);bottom.Children.Add(refresh);Grid.SetRow(bottom,2);root.Children.Add(bottom);
        }
        public void Render()
        {
            double offset=scroll.VerticalOffset;body.Children.Clear();
            subtitle.Text=app.Demo?"演示模式  /  以下全部为示例数据":"外设光环  /  电量，一眼可见";
            if(settingsOpen) {RenderSettings();scroll.ScrollToVerticalOffset(offset);return;}
            var devices=app.Devices;
            if(app.Demo) {
                var demo=new Border {Background=Palette.Brush("#30294B"),CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Margin=new Thickness(0,0,0,16)};
                demo.Child=Text("演示数据 · 用于预览界面和充电动效，不代表已连接设备。",11,Palette.Accent);body.Children.Add(demo);
            }
            DeviceReading featured=devices.FirstOrDefault(d=>app.IsPinned(d.Id) && d.Battery.HasValue) ?? devices.FirstOrDefault(d=>d.Battery.HasValue) ?? devices.FirstOrDefault();
            if(featured!=null) RenderHero(featured);else RenderEmpty();
            var label=new Grid {Margin=new Thickness(0,20,0,12)};label.ColumnDefinitions.Add(new ColumnDefinition());label.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});label.Children.Add(Text("我的设备",15,Palette.Text));var count=Text(devices.Count+" 个接口 / 设备",11,Palette.Muted);Grid.SetColumn(count,1);label.Children.Add(count);body.Children.Add(label);
            foreach(var device in devices) RenderCard(device);
            var info=Text("勾选「显示」将设备固定到系统托盘。Windows 可能将新图标收进 ^，可将其拖到任务栏。",11,Palette.Muted);info.Margin=new Thickness(2,8,2,4);info.LineHeight=18;body.Children.Add(info);
            footer.Text=app.Busy?"正在读取设备，请稍候…":app.Demo?"示例预览 · 不执行设备查询":app.LastRefresh==DateTime.MinValue?"等待首次读取":"更新于 "+app.LastRefresh.ToString("HH:mm:ss")+" · 每 "+app.Config.PollSeconds+" 秒";
            scroll.ScrollToVerticalOffset(offset);
        }
        private void RenderHero(DeviceReading d)
        {
            var hero=new Border {CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),BorderBrush=Palette.Brush("#303D56"),Padding=new Thickness(20),Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#202C43"),(Color)ColorConverter.ConvertFromString("#171F31"),45)};
            var layout=new StackPanel();var top=new Grid();top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});var eyebrow=Text("BATTERY AT A GLANCE",10,Palette.Muted);eyebrow.FontWeight=FontWeights.SemiBold;top.Children.Add(eyebrow);var tag=Pill(d.IsDemo?"DEMO":d.Battery.HasValue?"已读取":"待适配",Palette.Accent);Grid.SetColumn(tag,1);top.Children.Add(tag);layout.Children.Add(top);
            var center=new Grid {Margin=new Thickness(0,16,0,18)};center.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(140)});center.ColumnDefinitions.Add(new ColumnDefinition());
            center.Children.Add(new RingView(d,130,app.Config.Animations));var stats=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0)};
            var value=Text(d.Value,48,Palette.Brush(BatteryPolicy.Color(d.Battery)));value.FontWeight=FontWeights.Light;stats.Children.Add(value);var state=Text((d.Charge==ChargeState.Charging?"⚡  ":"●  ")+d.State,12,Palette.Brush(BatteryPolicy.Color(d.Battery)));stats.Children.Add(state);Grid.SetColumn(stats,1);center.Children.Add(stats);layout.Children.Add(center);
            var title=Text(d.Name,17,Palette.Text);title.FontWeight=FontWeights.SemiBold;layout.Children.Add(title);var meta=Text(d.Brand+"   ·   "+d.Connection,11,Palette.Muted);meta.Margin=new Thickness(0,6,0,0);layout.Children.Add(meta);hero.Child=layout;body.Children.Add(hero);
        }
        private void RenderCard(DeviceReading d)
        {
            var card=new Border {CornerRadius=new CornerRadius(12),Background=Palette.Brush("#182132"),BorderThickness=new Thickness(1),BorderBrush=Palette.Brush("#263147"),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10)};
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(56)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(68)});
            row.Children.Add(new RingView(d,44,app.Config.Animations) {VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,3,0,0)});
            var info=new StackPanel {Margin=new Thickness(3,0,8,0)};var name=Text(d.Name,13,Palette.Text);name.FontWeight=FontWeights.SemiBold;info.Children.Add(name);var state=Text(d.State+"  ·  "+d.Brand,10,Palette.Muted);state.Margin=new Thickness(0,5,0,0);info.Children.Add(state);
            var source=Text(d.Source,9,Palette.Muted);source.Margin=new Thickness(0,5,0,0);info.Children.Add(source);
            if(!String.IsNullOrEmpty(d.Detail)) {var detail=Text(d.Detail,10,Palette.Muted);detail.Margin=new Thickness(0,7,0,0);detail.LineHeight=17;info.Children.Add(detail);}
            Grid.SetColumn(info,1);row.Children.Add(info);
            var values=new StackPanel {HorizontalAlignment=HorizontalAlignment.Right};var value=Text(d.Value,19,Palette.Brush(BatteryPolicy.Color(d.Battery)));value.HorizontalAlignment=HorizontalAlignment.Right;values.Children.Add(value);
            var check=new CheckBox {Content="显示",IsChecked=app.IsPinned(d.Id),Margin=new Thickness(0,9,0,0),ToolTip="在右下角系统托盘显示此设备"};System.Windows.Automation.AutomationProperties.SetName(check,"在托盘显示 "+d.Name);
            check.Click+=delegate {app.Pin(d.Id,check.IsChecked==true);};values.Children.Add(check);Grid.SetColumn(values,2);row.Children.Add(values);card.Child=row;body.Children.Add(card);
        }
        private void RenderEmpty()
        {
            var panel=new Border {Background=Palette.Brush("#1B2539"),CornerRadius=new CornerRadius(16),Padding=new Thickness(24)};var content=new StackPanel();content.Children.Add(new RingView(new DeviceReading {Name="等待设备",Kind=DeviceKind.Mouse},96,false) {HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,10,0,20)});var title=Text(app.Busy?"正在寻找你的外设":"等待设备连接",20,Palette.Text);title.HorizontalAlignment=HorizontalAlignment.Center;content.Children.Add(title);var text=Text("插入罗技或雷蛇接收器，并唤醒无线设备。未找到支持的接口时，电量会保留为未知。",12,Palette.Muted);text.Margin=new Thickness(0,14,0,12);text.LineHeight=22;content.Children.Add(text);var demo=Button("预览界面与充电动效",delegate {app.SetDemo(true);});content.Children.Add(demo);panel.Child=content;body.Children.Add(panel);
        }
        private void Option(string title,string description,bool value,Action<bool> changed)
        {
            var panel=new Border {Background=Palette.Brush("#182132"),CornerRadius=new CornerRadius(10),Padding=new Thickness(15),Margin=new Thickness(0,0,0,10)};
            var stack=new StackPanel();var check=new CheckBox {Content=title,IsChecked=value,Foreground=Palette.Text,FontSize=13};check.Click+=delegate {changed(check.IsChecked==true);};stack.Children.Add(check);var text=Text(description,11,Palette.Muted);text.LineHeight=19;text.Margin=new Thickness(22,6,0,0);stack.Children.Add(text);panel.Child=stack;body.Children.Add(panel);
        }
        private void RenderSettings()
        {
            var title=Text("按你的习惯显示",24,Palette.Text);title.Margin=new Thickness(0,4,0,18);body.Children.Add(title);
            Option("显示所有设备","自动在托盘中显示新发现的设备，也可逐个勾选。",app.Config.PinAll,delegate(bool b) {app.Config.PinAll=b;app.Save();app.UpdateTray();Render();});
            Option("流畅动画","圆环柔和展开，充电时设备图标与闪电交替浮现。遵循 Windows 动画设置。",app.Config.Animations,delegate(bool b) {app.Config.Animations=b;app.Save();app.UpdateTray();});
            Option("低电量提醒","设备首次降至 20% 时提醒一次，充电或恢复至 25% 后重置。",app.Config.Notifications,delegate(bool b) {app.Config.Notifications=b;app.Save();});
            Option("登录 Windows 时启动","在当前账户登录时静默启动，可随时关闭。",Settings.StartupEnabled,delegate(bool b) {try {Settings.StartupEnabled=b;}catch(Exception ex) {MessageBox.Show(this,ex.Message,"无法修改启动项");}});
            var poll=new StackPanel {Margin=new Thickness(0,10,0,15)};poll.Children.Add(Text("刷新间隔",13,Palette.Text));var combo=new ComboBox {ItemsSource=new[] {15,30,60,120},SelectedItem=app.Config.PollSeconds,Margin=new Thickness(0,8,0,0),Height=30};combo.SelectionChanged+=delegate {if(combo.SelectedItem!=null) {app.Config.PollSeconds=(int)combo.SelectedItem;app.Save();app.RestartPolling();}};poll.Children.Add(combo);poll.Children.Add(Text("单位：秒。较长间隔可以减少设备唤醒。",10,Palette.Muted));body.Children.Add(poll);
            var legend=new StackPanel {Margin=new Thickness(0,6,0,16)};legend.Children.Add(Text("电量分层",13,Palette.Text));var chips=new WrapPanel {Margin=new Thickness(0,10,0,0)};foreach(var pair in new[] {Tuple.Create("75–100%",100),Tuple.Create("50–74%",60),Tuple.Create("21–49%",30),Tuple.Create("0–20%",10)}) {var chip=Pill(pair.Item1,Palette.Brush(BatteryPolicy.Color(pair.Item2)));chip.Margin=new Thickness(0,0,6,6);chips.Children.Add(chip);}legend.Children.Add(chips);body.Children.Add(legend);
            var buttons=new WrapPanel();var demo=Button(app.Demo?"返回真实设备":"预览演示",delegate {settingsOpen=false;app.SetDemo(!app.Demo);});demo.Margin=new Thickness(0,0,8,8);buttons.Children.Add(demo);var diag=Button("导出诊断",delegate {dismiss=false;try {app.ExportDiagnostics();}finally{dismiss=true;}});diag.Margin=new Thickness(0,0,8,8);buttons.Children.Add(diag);var taskbar=Button("任务栏设置",delegate {Process.Start("ms-settings:taskbar");});taskbar.Margin=new Thickness(0,0,8,8);buttons.Children.Add(taskbar);var quit=Button("退出应用",delegate {app.ExitApp();});quit.Margin=new Thickness(0,0,8,8);buttons.Children.Add(quit);body.Children.Add(buttons);
            var about=Text("PeripheralHalo 1.1.0 · Windows 11\n真实读数与估算值分别标注。没有读数时不生成百分比。G435 的 LIGHTSPEED 精确电量读取暂未实现。",10,Palette.Muted);about.LineHeight=19;about.Margin=new Thickness(0,10,0,10);body.Children.Add(about);footer.Text="设置自动保存在此电脑";
        }
        public void OpenPanel(bool settings=false)
        {
            settingsOpen=settings;Render();
            var screen=System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);var area=screen.WorkingArea;
            Show();var source=PresentationSource.FromVisual(this);double scaleX=source==null?1:source.CompositionTarget.TransformToDevice.M11,scaleY=source==null?1:source.CompositionTarget.TransformToDevice.M22;
            Width=Math.Min(468,area.Width/scaleX-20);Height=Math.Min(790,area.Height/scaleY-24);Left=area.Right/scaleX-Width-12;Top=area.Bottom/scaleY-Height-12;Activate();
            if(app.Config.Animations && SystemParameters.ClientAreaAnimation) {var translate=new TranslateTransform();root.RenderTransform=translate;translate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(14,0,TimeSpan.FromMilliseconds(220)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});root.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));}
        }
    }
}
