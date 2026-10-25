using ClipShot;
using Xunit;
namespace ClipShot.Checks;
public sealed class AnnotationTests
{
    [Theory][InlineData(90,3,90,0)][InlineData(2,90,0,90)][InlineData(-90,-2,-90,0)][InlineData(-2,-90,0,-90)]
    public void Arrow_snaps_near_horizontal_and_vertical(double x,double y,double expectedX,double expectedY){var p=AnnotationGeometry.SnapArrow(new(0,0),new(x,y));Assert.Equal(expectedX,p.X,6);Assert.Equal(expectedY,p.Y,6);}
    [Fact] public void Arrow_keeps_free_angle_away_from_guides(){var end=new ImagePoint(80,30);Assert.Equal(end,AnnotationGeometry.SnapArrow(new(0,0),end));}
    [Fact] public void Shift_forces_alignment(){var p=AnnotationGeometry.SnapArrow(new(0,0),new(80,30),true);Assert.Equal(80,p.X,6);Assert.Equal(0,p.Y,6);}
    [Fact] public void Diagonal_alignment_projects_onto_diagonal(){var p=AnnotationGeometry.SnapArrow(new(10,20),new(110,122));Assert.Equal(p.X-10,p.Y-20,6);}
    [Fact] public void Empty_arrow_does_not_produce_invalid_numbers(){var p=AnnotationGeometry.SnapArrow(new(5,5),new(5,5));Assert.Equal(new ImagePoint(5,5),p);}
    [Fact] public void Arrowhead_points_toward_requested_target(){var mark=new Annotation(MarkTool.Arrow,new(10,20),new(80,20));var points=AnnotationGeometry.ArrowHead(mark);Assert.Equal(mark.End,points[0]);Assert.All(points.Skip(1),p=>Assert.True(p.X<mark.End.X));}
    [Fact] public void Point_is_clamped_to_image(){Assert.Equal(new ImagePoint(0,19),new ImagePoint(-3,50).Clamp(30,20));}
    [Fact] public void Undo_redo_and_new_branch_are_consistent(){var h=new AnnotationHistory();var a=new Annotation(MarkTool.Arrow,new(1,1),new(20,20));var b=a with{Tool=MarkTool.Rectangle};h.Add(a);h.Add(b);h.Undo();Assert.Equal(a,Assert.Single(h.Marks));Assert.True(h.CanRedo);h.Redo();Assert.Equal(2,h.Marks.Count);h.Undo();h.Add(a with{Tool=MarkTool.Redact});Assert.False(h.CanRedo);h.Undo();h.Undo();h.Undo();Assert.Empty(h.Marks);h.Redo();Assert.Single(h.Marks);}
    private static ScreenFrame White(){byte[] pixels=Enumerable.Repeat((byte)255,100*80*4).ToArray();return new(0,0,100,80,pixels);}
    [Fact] public void Rendering_without_annotations_is_exact_and_independent(){var source=White();var rendered=AnnotationRenderer.Render(source,[]);Assert.Equal(source.Pixels,rendered.Pixels);Assert.NotSame(source.Pixels,rendered.Pixels);}
    [Fact] public void Arrow_changes_pixels_but_preserves_original(){var source=White();var rendered=AnnotationRenderer.Render(source,[new(MarkTool.Arrow,new(10,20),new(80,20),Width:4)]);int index=(20*100+40)*4;Assert.True(rendered.Pixels[index]<100);Assert.All(source.Pixels,p=>Assert.Equal(255,p));Assert.Equal(source.Pixels.Take(100*4),rendered.Pixels.Take(100*4));}
    [Fact] public void Rectangle_has_clear_interior(){var rendered=AnnotationRenderer.Render(White(),[new(MarkTool.Rectangle,new(10,10),new(80,60),Width:3)]);Assert.True(rendered.Pixels[(10*100+40)*4]<100);Assert.Equal(255,rendered.Pixels[(30*100+40)*4]);}
    [Fact] public void Redaction_is_opaque_and_limited_to_region(){var rendered=AnnotationRenderer.Render(White(),[new(MarkTool.Redact,new(20,20),new(40,40))]);Assert.Equal(32,rendered.Pixels[(30*100+30)*4]);Assert.Equal(255,rendered.Pixels[(30*100+30)*4+3]);Assert.Equal(255,rendered.Pixels[(10*100+10)*4]);}
    [Fact] public void Caption_is_drawn_and_original_is_unchanged(){var source=White();var rendered=AnnotationRenderer.Render(source,[new(MarkTool.Text,new(5,5),new(5,5),"Тест",Width:3)]);Assert.False(rendered.Pixels.SequenceEqual(source.Pixels));Assert.All(source.Pixels,p=>Assert.Equal(255,p));}
    [Fact] public void Pixelation_changes_pattern_only_inside_selected_region(){var source=White();for(int y=20;y<60;y++)for(int x=20;x<80;x++){int i=(y*100+x)*4;source.Pixels[i]=source.Pixels[i+1]=source.Pixels[i+2]=(byte)((x+y)%2==0?0:255);}var rendered=AnnotationRenderer.Render(source,[new(MarkTool.Blur,new(20,20),new(80,60))]);Assert.NotEqual(source.Pixels[(30*100+30)*4],rendered.Pixels[(30*100+30)*4]);Assert.Equal(source.Pixels.Take(20*100*4),rendered.Pixels.Take(20*100*4));}
}
