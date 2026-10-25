using ClipShot;
using Xunit;
namespace ClipShot.Checks;
public sealed class LanguageTests
{
    [Theory][InlineData(null)][InlineData("")][InlineData("ru")][InlineData("invalid")][InlineData("en-US")]
    public void Russian_is_default_unless_English_is_explicitly_selected(string? language){var text=new UiText(language);Assert.False(text.English);Assert.Equal("Справка",text.Translate("Справка"));Assert.Equal("Я верю в тебя, ты разберёшься сам.",text.Translate("Я верю в тебя, ты разберёшься сам."));}
    [Fact] public void English_is_opt_in(){var text=new UiText("en");Assert.True(text.English);Assert.Equal("Help",text.Translate("Справка"));Assert.Equal("Search history",text.Translate("Поиск по истории"));Assert.Equal("Templates",text.Translate("Шаблоны"));}
    [Theory][InlineData("test@example.com")][InlineData("User's own text")][InlineData("Мои данные 123")]
    public void Unknown_text_is_not_modified(string value)=>Assert.Equal(value,new UiText("en").Translate(value));
    [Fact] public void Status_message_can_return_to_Russian(){string source="Не удалось скопировать. Повторите выбор.";Assert.Equal(source,UiText.Russian(new UiText("en").Translate(source)));}
    [Theory][InlineData(112u,false,"Alt+F1")][InlineData(123u,true,"Ctrl+Alt+F12")]
    public void Tray_shortcut_description_reflects_real_keys(uint key,bool control,string expected)=>Assert.Equal(expected,Win32Hotkey.Describe(key,control));
}
