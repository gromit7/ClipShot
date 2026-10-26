using ClipShot;
using Xunit;
namespace ClipShot.Checks;
public sealed class SettingsTests
{
    [Theory][InlineData("broken",260)][InlineData("",260)][InlineData("999999999999",260)][InlineData("-1",220)][InlineData("0",220)][InlineData("520",520)][InlineData("100000",4096)]public void Width_is_safe(string value,int expected)=>Assert.Equal(expected,AppSettings.WindowWidth(value));
    [Theory][InlineData("broken",220)][InlineData("0",100)][InlineData("900",900)][InlineData("100000",4096)]public void Height_is_safe(string value,int expected)=>Assert.Equal(expected,AppSettings.WindowHeight(value));
    [Theory][InlineData("0",0)][InlineData("30",30)][InlineData("90",90)][InlineData("-30",90)][InlineData("2147483647",90)][InlineData("broken",90)]public void Retention_falls_back_safely(string value,int expected)=>Assert.Equal(expected,AppSettings.RetentionDays(value));
    [Theory][InlineData("112",112u)][InlineData("123",123u)][InlineData("111",113u)][InlineData("124",113u)][InlineData("-1",113u)][InlineData("broken",113u)][InlineData("4294967295",113u)]public void Function_key_is_limited_to_f1_through_f12(string value,uint expected)=>Assert.Equal(expected,AppSettings.FunctionKey(value,113));
    [Theory][InlineData("--smoke-test")][InlineData("--demo")][InlineData("--capture-self-test")][InlineData("--capture-demo")][InlineData("--shell-test")][InlineData("--upgrade-self-test")][InlineData("--interaction-self-test")][InlineData("--editor-self-test")][InlineData("--editor-demo")]public void Every_test_mode_is_isolated(string flag)=>Assert.True(TestEnvironment.IsIsolated(["app",flag]));
    [Fact] public void Normal_launch_is_not_isolated()=>Assert.False(TestEnvironment.IsIsolated(["app","--background"]));
    [Fact] public void Production_ignores_test_path_override()=>Assert.EndsWith("ClipNest",TestEnvironment.DataFolder(false,"C:/unexpected"));
    [Fact] public void Test_path_is_restricted_to_temp_test_child(){string path=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Assert.Equal(Path.GetFullPath(path),TestEnvironment.DataFolder(true,path));Assert.Throws<ArgumentException>(()=>TestEnvironment.DataFolder(true,Path.Combine(path,"..","..","ClipNest")));Assert.Throws<ArgumentException>(()=>TestEnvironment.DataFolder(true,TestEnvironment.DataFolder(false)));}
    [Fact] public void Test_messages_cannot_open_or_close_production_instance(){Assert.NotEqual(TestEnvironment.MessageName(true,"Quit"),TestEnvironment.MessageName(false,"Quit"));Assert.NotEqual(TestEnvironment.MessageName(true,"Show"),TestEnvironment.MessageName(false,"Show"));Assert.Equal("ClipNest.Quit.v1",TestEnvironment.MessageName(false,"Quit"));}
}
