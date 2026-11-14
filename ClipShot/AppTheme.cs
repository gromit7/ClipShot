namespace ClipShot;

public sealed record AppTheme(string Id,string Name,bool Dark,string Window,string Header,string Search,string Separator,string Selection,string Secondary,string Primary,string SelectedText,string Title,string Font="Segoe UI Variable",bool Acrylic=false,bool Gradient=false,int Corner=6)
{
    public bool Retro=>Id is "Windows98" or "WindowsXP";
    public bool Classic=>Id=="Windows98";
}
public static class AppThemes
{
    public static readonly IReadOnlyList<AppTheme> All=Array.AsReadOnly(new AppTheme[]{
        new("Gradient","Тёмный градиент",true,"#202A3C","#20293A","#30384B","#4A5266","#46516B","#BBC4D6","#F3F5FA","#FFFFFF","#FFFFFF",Gradient:true,Corner:10),
        new("Transparent","Полупрозрачная (акрил)",true,"#B8242529","#E0202125","#F22E3035","#805E6572","#F04D5668","#E0E4EE","#FFFFFF","#FFFFFF","#FFFFFF",Acrylic:true,Corner:10),
        new("White","Светлая",false,"#FFFFFF","#F3F4F6","#FFFFFF","#D5D9E0","#E1E7EF","#59616D","#16181C","#16181C","#16181C"),
        new("Black","Чёрная OLED",true,"#000000","#0B0B0B","#151515","#333333","#292929","#B7B7B7","#F5F5F5","#FFFFFF","#F5F5F5",Corner:0),
        new("Dark","Тёмная графитовая",true,"#242529","#202125","#2E3035","#3A3C42","#373A41","#ADB4C1","#F0F1F4","#FFFFFF","#F0F1F4",Corner:4),
        new("Windows98","Windows 98",false,"#C0C0C0","#000080","#FFFFFF","#808080","#000080","#454545","#000000","#FFFFFF","#FFFFFF","Tahoma",Corner:0),
        new("WindowsXP","Windows XP",false,"#ECE9D8","#0054E3","#FFFFFF","#ACA899","#316AC5","#4C4C4C","#111111","#FFFFFF","#FFFFFF","Tahoma",Corner:3),
        new("CatPink","Розовая с котиками",false,"#FFF2F7","#FCE4EE","#FFF9FC","#E2B9CA","#F5C7DB","#775268","#352735","#352735","#352735",Corner:10),
        new("WarmLight","Светлая бежевая",false,"#F5F0E7","#E9DDC9","#FFFCF5","#B8AA95","#DAC7A8","#625440","#282016","#282016","#282016",Corner:8),
        new("NightBlue","Тёмная синяя",true,"#142439","#0D1B2D","#20344E","#46617E","#365778","#B9CEE7","#F2F7FF","#FFFFFF","#F2F7FF",Corner:8),
        new("MintLight","Светлая зелёная",false,"#EDF5EF","#D8EADC","#F8FFF9","#9AB7A2","#B9D9C2","#3E6049","#163320","#163320","#163320",Corner:8)
    });
    public static AppTheme Resolve(string id,bool systemDark=false)=>All.FirstOrDefault(t=>t.Id==id)??All.First(t=>t.Id==(id=="Light"?"White":id=="Default"&&!systemDark?"White":"Dark"));
}
