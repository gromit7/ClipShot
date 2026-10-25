param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$references=@("System.Drawing.Common","System.Drawing.Primitives");foreach($dependency in "System.Private.Windows.GdiPlus","System.Private.Windows.Core"){try{$references+=[Reflection.Assembly]::Load($dependency).Location}catch{}}
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class ClipShotIconBuilder
{
    private static Bitmap Resize(Bitmap source,int size)
    {
        int left=source.Width,top=source.Height,right=0,bottom=0;
        for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
            if(source.GetPixel(x,y).A>16){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);}
        if(right<=left||bottom<=top)throw new InvalidDataException("Empty icon source");
        var output=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(output))
        using(var attributes=new ImageAttributes())
        {
            graphics.Clear(Color.Transparent);graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;graphics.CompositingQuality=CompositingQuality.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            double factor=size*.92/Math.Max(right-left,bottom-top);
            int width=Math.Max(1,(int)Math.Round((right-left)*factor)),height=Math.Max(1,(int)Math.Round((bottom-top)*factor));
            graphics.DrawImage(source,new Rectangle((size-width)/2,(size-height)/2,width,height),left,top,right-left,bottom-top,GraphicsUnit.Pixel,attributes);
        }
        return output;
    }
    private static byte[] Dib(Bitmap bitmap)
    {
        int size=bitmap.Width,stride=((size+31)/32)*4;
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
        {
            writer.Write(40);writer.Write(size);writer.Write(size*2);writer.Write((ushort)1);writer.Write((ushort)32);
            writer.Write(0);writer.Write(size*size*4);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
            for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){var c=bitmap.GetPixel(x,y);writer.Write(c.B);writer.Write(c.G);writer.Write(c.R);writer.Write(c.A);}
            for(int y=size-1;y>=0;y--){var row=new byte[stride];for(int x=0;x<size;x++)if(bitmap.GetPixel(x,y).A==0)row[x/8]|=(byte)(0x80>>(x%8));writer.Write(row);}
            return stream.ToArray();
        }
    }
    public static void Build(string sourcePath,string iconPath,string pngPath)
    {
        int[] sizes={16,20,24,32,40,48,64,96,128,256};var frames=new byte[sizes.Length][];
        using(var source=new Bitmap(sourcePath))
        {
            for(int index=0;index<sizes.Length;index++)using(var bitmap=Resize(source,sizes[index]))frames[index]=Dib(bitmap);
            if(!String.IsNullOrEmpty(pngPath))using(var bitmap=Resize(source,128))bitmap.Save(pngPath,ImageFormat.Png);
        }
        using(var stream=File.Create(iconPath))using(var writer=new BinaryWriter(stream))
        {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int index=0;index<sizes.Length;index++)
            {writer.Write((byte)(sizes[index]==256?0:sizes[index]));writer.Write((byte)(sizes[index]==256?0:sizes[index]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[index].Length);writer.Write(offset);offset+=frames[index].Length;}
            foreach(var frame in frames)writer.Write(frame);
        }
    }
}
'@
$project=Join-Path (Split-Path $PSScriptRoot) 'ClipShot'
$assets=Join-Path $project 'Assets'
[ClipShotIconBuilder]::Build((Join-Path $project 'Branding/MicrophoneOn.png'),(Join-Path $assets 'ClipShot.ico'),(Join-Path $assets 'ClipShot.png'))
[ClipShotIconBuilder]::Build((Join-Path $project 'Branding/MicrophoneOff.png'),(Join-Path $assets 'ClipShot.Muted.ico'),$null)
Write-Output 'Built supplied icon pair: 16–256 px, alpha preserved.'
