namespace ClipShot;

public sealed record AppTheme(string Id,string Name,bool Dark,string Window,string Header,string Search,string Separator,string Selection,string Secondary,string Primary,string SelectedText,string Title,string Font="Segoe UI Variable",bool Acrylic=false,bool Gradient=false,int Corner=6);
public static class AppThemes
{
    public static readonly IReadOnlyList<AppTheme> All=Array.AsReadOnly(new AppTheme[]{
        new("Gradient","Градиентная",true,"#202A3C","#20293A","#30384B","#4A5266","#46516B","#BBC4D6","#F3F5FA","#FFFFFF","#FFFFFF",Gradient:true),
        new("Transparent","Прозрачная",true,"#00242529","#B0202125","#DE2E3035","#805E6572","#D04D5668","#CCD1DD","#FFFFFF","#FFFFFF","#FFFFFF",Acrylic:true),
        new("White","Белая",false,"#FFFFFF","#F3F4F6","#FFFFFF","#D5D9E0","#E1E7EF","#59616D","#16181C","#16181C","#16181C"),
        new("Black","Чёрная",true,"#000000","#0B0B0B","#151515","#333333","#292929","#B7B7B7","#F5F5F5","#FFFFFF","#F5F5F5"),
        new("Dark","Тёмная",true,"#242529","#202125","#2E3035","#3A3C42","#373A41","#ADB4C1","#F0F1F4","#FFFFFF","#F0F1F4"),
        new("Windows98","Windows 98",false,"#C0C0C0","#000080","#FFFFFF","#808080","#000080","#454545","#000000","#FFFFFF","#FFFFFF","Tahoma",Corner:0),
        new("WindowsXP","Windows XP",false,"#ECE9D8","#0054E3","#FFFFFF","#ACA899","#316AC5","#4C4C4C","#111111","#FFFFFF","#FFFFFF","Tahoma",Corner:3)
    });
    public static AppTheme Resolve(string id,bool systemDark=false)=>All.FirstOrDefault(t=>t.Id==id)??All.First(t=>t.Id==(id=="Light"?"White":id=="Default"&&!systemDark?"White":"Dark"));
}
