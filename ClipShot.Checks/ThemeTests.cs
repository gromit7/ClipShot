using ClipShot;
using Xunit;
namespace ClipShot.Checks;
public sealed class ThemeTests
{
    [Fact] public void Exactly_seven_themes_have_unique_ids_and_names(){Assert.Equal(7,AppThemes.All.Count);Assert.Equal(7,AppThemes.All.Select(t=>t.Id).Distinct().Count());Assert.Equal(7,AppThemes.All.Select(t=>t.Name).Distinct().Count());}
    [Theory][InlineData("Gradient")][InlineData("Transparent")][InlineData("White")][InlineData("Black")][InlineData("Dark")][InlineData("Windows98")][InlineData("WindowsXP")]
    public void Every_palette_has_valid_colors_and_readable_text(string id)
    {
        var t=AppThemes.Resolve(id);Assert.Equal(id,t.Id);
        foreach(string color in new[]{t.Window,t.Header,t.Search,t.Separator,t.Selection,t.Secondary,t.Primary,t.SelectedText,t.Title})Assert.Matches("^#([0-9A-F]{6}|[0-9A-F]{8})$",color);
        Assert.True(Contrast(t.Primary,t.Search)>=4.5,"Main text contrast");Assert.True(Contrast(t.SelectedText,t.Selection)>=4.5,"Selected text contrast");Assert.True(Contrast(t.Title,t.Header)>=4.5,"Title contrast");
    }
    [Fact] public void Old_light_setting_migrates_to_white()=>Assert.Equal("White",AppThemes.Resolve("Light").Id);
    [Fact] public void Old_system_setting_respects_current_system(){Assert.Equal("White",AppThemes.Resolve("Default",false).Id);Assert.Equal("Dark",AppThemes.Resolve("Default",true).Id);}
    [Fact] public void Unknown_theme_has_safe_fallback()=>Assert.Equal("Dark",AppThemes.Resolve("broken").Id);
    [Fact] public void Transparency_is_desktop_material_and_gradient_is_distinct(){Assert.True(AppThemes.Resolve("Transparent").Acrylic);Assert.True(AppThemes.Resolve("Gradient").Gradient);Assert.NotEqual(AppThemes.Resolve("Black").Window,AppThemes.Resolve("Dark").Window);}
    private static double Contrast(string a,string b){double L(string s){uint n=Convert.ToUInt32(s.TrimStart('#'),16);double C(int shift){double v=((n>>shift)&255)/255.0;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*C(16)+.7152*C(8)+.0722*C(0);}double x=L(a),y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
}
