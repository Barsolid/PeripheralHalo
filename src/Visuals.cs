// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Drawing=System.Drawing;

namespace PeripheralHalo
{
    public static class Palette
    {
        public static SolidColorBrush Brush(string hex) {return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}
        public static readonly Brush Text=Brush("#EDF1FC"),Muted=Brush("#8D99B2"),Accent=Brush("#A69CFF"),Line=Brush("#283248");
        public static Geometry Glyph(DeviceKind kind)
        {
            if(kind==DeviceKind.Mouse) return Geometry.Parse("M16,3 C10,3 7,7 7,13 L7,21 C7,27 11,30 16,30 C21,30 25,27 25,21 L25,13 C25,7 22,3 16,3 Z M16,4 L16,13 M8,14 L24,14 M16,8 L16,10");
            if(kind==DeviceKind.Headset) return Geometry.Parse("M5,21 L5,15 C5,1 27,1 27,15 L27,21 M5,16 L9,16 L9,26 L5,26 Q2,26 2,23 L2,20 Q2,16 5,16 M27,16 L23,16 L23,26 L27,26 Q30,26 30,23 L30,20 Q30,16 27,16");
            if(kind==DeviceKind.Keyboard) return Geometry.Parse("M3,8 L29,8 Q31,8 31,11 L31,24 Q31,27 28,27 L4,27 Q1,27 1,24 L1,11 Q1,8 3,8 M6,14 L7,14 M12,14 L13,14 M18,14 L19,14 M24,14 L25,14 M6,19 L7,19 M12,19 L13,19 M18,19 L19,19 M24,19 L25,19 M9,23 L23,23");
            return Geometry.Parse("M9,3 L23,3 Q26,3 26,6 L26,26 Q26,29 23,29 L9,29 Q6,29 6,26 L6,6 Q6,3 9,3 M13,25 L19,25 M12,8 L20,8 M12,13 L20,13");
        }
        public static Geometry Bolt { get {return Geometry.Parse("M19,2 L8,18 L15,18 L12,31 L25,13 L18,13 Z");} }
    }
    public sealed class RingView : FrameworkElement
    {
        public static readonly DependencyProperty ProgressProperty=DependencyProperty.Register("Progress",typeof(double),typeof(RingView),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PhaseProperty=DependencyProperty.Register("Phase",typeof(double),typeof(RingView),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
        public double Progress {get{return (double)GetValue(ProgressProperty);}set{SetValue(ProgressProperty,value);}}
        public double Phase {get{return (double)GetValue(PhaseProperty);}set{SetValue(PhaseProperty,value);}}
        public DeviceReading Reading;
        private readonly bool animations;
        public RingView(DeviceReading reading,double size,bool animate)
        {
            Reading=reading;animations=animate;Width=Height=size;ToolTip=reading.Name+" · "+reading.Value+" · "+reading.State;
            System.Windows.Automation.AutomationProperties.SetName(this,reading.Name+"，"+reading.Value+"，"+reading.State);
            Loaded+=delegate {
                double target=Reading.Battery.HasValue?Reading.Battery.Value/100.0:0;
                if(animations && SystemParameters.ClientAreaAnimation) BeginAnimation(ProgressProperty,new DoubleAnimation(0,target,TimeSpan.FromMilliseconds(750)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});else Progress=target;
                if(animations && SystemParameters.ClientAreaAnimation && Reading.Charge==ChargeState.Charging) BeginAnimation(PhaseProperty,new DoubleAnimation(0,1,TimeSpan.FromSeconds(1.3)) {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase {EasingMode=EasingMode.EaseInOut}});
            };
            Unloaded+=delegate {BeginAnimation(ProgressProperty,null);BeginAnimation(PhaseProperty,null);};
        }
        protected override void OnRender(DrawingContext dc)
        {
            double s=Math.Min(ActualWidth,ActualHeight),c=s/2,thickness=s>100?7:4,r=c-thickness-2;
            Brush color=Palette.Brush(BatteryPolicy.Color(Reading.Battery));
            dc.DrawEllipse(null,new Pen(Palette.Brush("#283449"),thickness),new Point(c,c),r,r);
            if(Reading.Battery.HasValue && Progress>0) {
                var pen=new Pen(color,thickness) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
                double a=Math.Min(0.99999,Progress)*2*Math.PI;
                var g=new StreamGeometry();using(var ctx=g.Open()) {ctx.BeginFigure(new Point(c,c-r),false,false);ctx.ArcTo(new Point(c+r*Math.Sin(a),c-r*Math.Cos(a)),new Size(r,r),0,a>Math.PI,SweepDirection.Clockwise,true,false);}g.Freeze();
                dc.DrawGeometry(null,pen,g);
            }
            if(s>100) {dc.DrawEllipse(null,new Pen(Palette.Brush("#253145"),1),new Point(c,c),r-14,r-14);}
            double icon=s>100?44:s*0.43;
            double phase=Reading.Charge==ChargeState.Charging?(animations&&SystemParameters.ClientAreaAnimation?Phase:1):0;
            dc.PushTransform(new TranslateTransform(c-icon/2,c-icon/2));dc.PushTransform(new ScaleTransform(icon/32,icon/32));
            dc.PushOpacity(1-phase*0.88);dc.DrawGeometry(null,new Pen(Reading.Battery.HasValue?Palette.Text:Palette.Muted,2) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},Palette.Glyph(Reading.Kind));dc.Pop();
            if(Reading.Charge==ChargeState.Charging) {dc.PushOpacity(phase);dc.PushTransform(new TranslateTransform(0,-2*phase));dc.DrawGeometry(color,null,Palette.Bolt);dc.Pop();dc.Pop();}
            dc.Pop();dc.Pop();
        }
    }
    public static class TrayArt
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        public static Drawing.Icon Create(DeviceReading d,double phase)
        {
            using(var bitmap=new Drawing.Bitmap(64,64))
            using(var g=Drawing.Graphics.FromImage(bitmap)) {
                g.SmoothingMode=Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(Drawing.Color.Transparent);
                Drawing.Color color=Drawing.ColorTranslator.FromHtml(BatteryPolicy.Color(d.Battery));
                using(var track=new Drawing.Pen(Drawing.Color.FromArgb(160,100,115,139),6)) g.DrawEllipse(track,4,4,56,56);
                if(d.Battery.HasValue && d.Battery>0) using(var ring=new Drawing.Pen(color,6) {StartCap=Drawing.Drawing2D.LineCap.Round,EndCap=Drawing.Drawing2D.LineCap.Round}) g.DrawArc(ring,4,4,56,56,-90,Math.Min(359.9f,d.Battery.Value*3.6f));
                var light=Drawing.Color.FromArgb((int)(255*(1-phase*0.86)),238,243,255);
                using(var pen=new Drawing.Pen(light,3.4f) {StartCap=Drawing.Drawing2D.LineCap.Round,EndCap=Drawing.Drawing2D.LineCap.Round,LineJoin=Drawing.Drawing2D.LineJoin.Round}) {
                    if(d.Kind==DeviceKind.Mouse) {g.DrawEllipse(pen,23,16,18,32);g.DrawLine(pen,32,18,32,29);g.DrawLine(pen,24,30,40,30);}
                    else if(d.Kind==DeviceKind.Headset) {g.DrawArc(pen,19,17,26,28,180,180);g.DrawRectangle(pen,18,31,6,13);g.DrawRectangle(pen,40,31,6,13);}
                    else if(d.Kind==DeviceKind.Keyboard) {g.DrawRectangle(pen,16,23,32,20);g.DrawLine(pen,22,29,25,29);g.DrawLine(pen,31,29,34,29);g.DrawLine(pen,40,29,43,29);g.DrawLine(pen,25,37,39,37);}
                    else {g.DrawEllipse(pen,23,23,18,18);g.DrawEllipse(pen,30,30,4,4);}
                }
                if(d.Charge==ChargeState.Charging) using(var b=new Drawing.SolidBrush(Drawing.Color.FromArgb((int)(255*phase),color))) g.FillPolygon(b,new[] {new Drawing.PointF(36,15),new Drawing.PointF(23,34),new Drawing.PointF(31,34),new Drawing.PointF(27,50),new Drawing.PointF(43,28),new Drawing.PointF(34,28)});
                IntPtr handle=bitmap.GetHicon();try {using(var icon=Drawing.Icon.FromHandle(handle)) return (Drawing.Icon)icon.Clone();}finally{DestroyIcon(handle);}
            }
        }
    }
}
