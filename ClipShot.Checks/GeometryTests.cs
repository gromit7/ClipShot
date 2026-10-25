using ClipShot;
using Xunit;
namespace ClipShot.Checks;
public sealed class GeometryTests
{
    [Theory][InlineData(2,1,5,4)][InlineData(5,4,2,1)][InlineData(5,1,2,4)][InlineData(2,4,5,1)]
    public void Selection_works_in_every_drag_direction(int ax,int ay,int bx,int by)=>Assert.Equal(new PixelRegion(2,1,3,3),PixelRegion.FromDrag(ax,ay,bx,by,10,10));
    [Theory][InlineData(-9,-2,19,20,0,0,10,8)][InlineData(-9,2,-1,4,0,2,0,2)][InlineData(20,2,30,4,10,2,0,2)][InlineData(2,2,2,2,2,2,0,0)][InlineData(int.MinValue,int.MinValue,int.MaxValue,int.MaxValue,0,0,10,8)]
    public void Selection_clamps_to_screen(int ax,int ay,int bx,int by,int x,int y,int w,int h)=>Assert.Equal(new PixelRegion(x,y,w,h),PixelRegion.FromDrag(ax,ay,bx,by,10,8));
    [Fact] public void Crop_preserves_negative_monitor_origin_and_exact_pixels(){byte[] pixels=Enumerable.Range(0,48).Select(i=>(byte)i).ToArray();var crop=new ScreenFrame(-1920,-120,4,3,pixels).Crop(new(1,1,2,2));Assert.Equal(-1919,crop.X);Assert.Equal(-119,crop.Y);Assert.Equal(pixels.Skip(20).Take(8).Concat(pixels.Skip(36).Take(8)),crop.Pixels);}
    [Fact] public void Full_frame_crop_is_independent_copy(){byte[] pixels=Enumerable.Range(0,16).Select(i=>(byte)i).ToArray();var crop=new ScreenFrame(0,0,2,2,pixels).Crop(new(0,0,2,2));Assert.Equal(pixels,crop.Pixels);pixels[0]=255;Assert.Equal(0,crop.Pixels[0]);}
    [Fact] public void Bottom_right_pixel_is_valid(){var crop=new ScreenFrame(0,0,2,2,Enumerable.Range(0,16).Select(i=>(byte)i).ToArray()).Crop(new(1,1,1,1));Assert.Equal(new byte[]{12,13,14,15},crop.Pixels);}
    [Theory][InlineData(-1,0,1,1)][InlineData(0,-1,1,1)][InlineData(0,0,0,1)][InlineData(0,0,1,0)][InlineData(3,0,2,1)][InlineData(0,2,1,2)][InlineData(1,0,int.MaxValue,1)][InlineData(int.MaxValue,0,2,1)]
    public void Invalid_and_overflowing_crop_is_rejected(int x,int y,int w,int h)=>Assert.Throws<ArgumentOutOfRangeException>(()=>new ScreenFrame(0,0,4,3,new byte[48]).Crop(new(x,y,w,h)));
    [Fact] public void Malformed_frame_is_rejected_before_copying()=>Assert.Throws<ArgumentException>(()=>new ScreenFrame(0,0,4,3,new byte[4]).Crop(new(0,0,1,1)));
    [Fact] public void Random_crops_match_reference_pixel_by_pixel(){var random=new Random(731);for(int n=0;n<100;n++){int w=random.Next(1,25),h=random.Next(1,25);var pixels=new byte[w*h*4];random.NextBytes(pixels);int x=random.Next(w),y=random.Next(h),cw=random.Next(1,w-x+1),ch=random.Next(1,h-y+1);var crop=new ScreenFrame(-50,20,w,h,pixels).Crop(new(x,y,cw,ch));var expected=new List<byte>();for(int cy=y;cy<y+ch;cy++)for(int cx=x;cx<x+cw;cx++)expected.AddRange(pixels.Skip((cy*w+cx)*4).Take(4));Assert.Equal(expected,crop.Pixels);}}
    [Theory][InlineData(false,0x4001u)][InlineData(true,0x4003u)] public void Hotkey_uses_alt_control_and_no_repeat(bool ctrl,uint expected)=>Assert.Equal(expected,Win32Hotkey.Modifiers(ctrl));
}
