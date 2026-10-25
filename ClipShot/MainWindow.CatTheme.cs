using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ClipShot;
public sealed partial class MainWindow
{
    private Canvas? catArtwork;
    private void ApplyCatTheme()
    {
        if(catArtwork is null)
        {
            // Декор занимает только свободный левый край существующей строки обновления.
            // Фиксированные 22 DIP не увеличивают её высоту; клипы и поле поиска не затрагиваем.
            catArtwork=CreateCatArtwork();
            Grid.SetRow(catArtwork,Grid.GetRow(ManualUpdateLink));Root.Children.Add(catArtwork);
            Root.SizeChanged+=(_,_)=>PositionCatArtwork();
            ManualUpdateLink.SizeChanged+=(_,_)=>PositionCatArtwork();
        }
        PositionCatArtwork();
    }
    private void PositionCatArtwork()
    {
        if(catArtwork is null)return;
        // Длинный статус загрузки получает всю строку: котики не перекрывают действие.
        catArtwork.Visibility=activeTheme.Id=="CatPink"&&Root.ActualWidth-ManualUpdateLink.ActualWidth>=88
            ?Visibility.Visible:Visibility.Collapsed;
    }
    private static Canvas CreateCatArtwork()
    {
        var artwork=new Canvas{Width=76,Height=22,Margin=new Thickness(7,0,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        AutomationProperties.SetAccessibilityView(artwork,AccessibilityView.Raw);
        SolidColorBrush Brush(string color)=>new(ThemeColor(color));
        void Ellipse(double x,double y,double width,double height,string fill)
        {
            var shape=new Ellipse{Width=width,Height=height,Fill=Brush(fill)};
            Canvas.SetLeft(shape,x);Canvas.SetTop(shape,y);artwork.Children.Add(shape);
        }
        void Line(double x1,double y1,double x2,double y2,string color,double thickness=1)
            =>artwork.Children.Add(new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=Brush(color),StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
        void Triangle(string color,params Point[] points)
        {
            var shape=new Polygon{Fill=Brush(color)};foreach(var point in points)shape.Points.Add(point);artwork.Children.Add(shape);
        }
        void Cat(double x,bool ginger)
        {
            string fur=ginger?"#C86B28":"#28232C",shade=ginger?"#8B451F":"#17131C";
            var tail=new PathGeometry();var figure=new PathFigure{StartPoint=new(x+1,19)};
            figure.Segments.Add(new BezierSegment{Point1=new(x-7,23),Point2=new(x-7,10),Point3=new(x-3,12)});tail.Figures.Add(figure);
            artwork.Children.Add(new Microsoft.UI.Xaml.Shapes.Path{Data=tail,Stroke=Brush(fur),StrokeThickness=2.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
            Ellipse(x,10,15,11,fur);Ellipse(x+1,18,6,4,shade);Ellipse(x+9,18,6,4,shade);
            Triangle(fur,new(x-1,8),new(x,0),new(x+7,5));Triangle(fur,new(x+8,5),new(x+16,0),new(x+17,9));
            Triangle("#EDB4C1",new(x+1,6),new(x+1,2.5),new(x+4,5));Triangle("#EDB4C1",new(x+11,5),new(x+15,2.5),new(x+15,6));
            Ellipse(x-1,3,18,12,fur);
            Ellipse(x+3,7,3.2,2.4,ginger?"#FFF4D6":"#E8D779");Ellipse(x+10,7,3.2,2.4,ginger?"#FFF4D6":"#E8D779");
            Ellipse(x+4.2,7,1,2.2,"#231922");Ellipse(x+11.1,7,1,2.2,"#231922");
            Triangle(ginger?"#713B2A":"#F0B3C6",new(x+6.5,10),new(x+9.5,10),new(x+8,11.8));
            Line(x+8,11.8,x+6.7,12.8,ginger?shade:"#BC9CAD",.65);Line(x+8,11.8,x+9.3,12.8,ginger?shade:"#BC9CAD",.65);
            if(ginger)
            {
                // Рыжие полоски различимы и без цвета фона; второй кот остаётся чёрным.
                Line(x+6,3.9,x+6.8,5.6,shade,.9);Line(x+9.5,3.9,x+9,5.6,shade,.9);
                Line(x+1,15,x+3.7,16,shade,1);Line(x+11,15.2,x+13.8,14.4,shade,1);
            }
        }
        Cat(10,false);Cat(48,true);
        // Маленький розовый цветок между котиками, без добавления текста или emoji.
        foreach(var petal in new[]{new Point(35,13),new Point(38,13),new Point(36.5,10.5),new Point(36.5,15.5)})Ellipse(petal.X,petal.Y,3.2,3.2,"#E8A6BF");
        Ellipse(37,13.5,2.2,2.2,"#C78B44");
        return artwork;
    }
}
