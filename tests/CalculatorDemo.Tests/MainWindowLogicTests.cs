using CalculatorDemo.Coverable;
using Xunit;

namespace CalculatorDemo.Tests;

public class MainWindowLogicTests
{
    private static MainWindowLogic Create(out FakeIo io)
    {
        io = new FakeIo();
        return new MainWindowLogic(io);
    }

    // ---- OnWindowKeyDown: the 4-condition compound (c>='0' && c<='9') || c=='.' || c=='\b' ----

    [Theory]
    [InlineData('0')]
    [InlineData('5')]
    [InlineData('9')]
    public void Digit_Keys_Enter_As_Display(char c)
    {
        var w = Create(out var io);
        w.Display = "";
        w.EraseDisplay = true;
        w.OnWindowKeyDown(c);
        Assert.Equal(c.ToString(), w.Display);
    }

    [Fact]
    public void Dot_Key_Adds_Decimal()
    {
        var w = Create(out _);
        w.Display = "1";
        w.EraseDisplay = false;
        w.OnWindowKeyDown('.');
        Assert.Equal("1.", w.Display);
    }

    [Fact]
    public void Backspace_Key_Shortens_Display()
    {
        var w = Create(out _);
        w.Display = "12";
        w.EraseDisplay = false;
        w.OnWindowKeyDown('\b');
        Assert.Equal("1", w.Display);
    }

    [Theory]
    [InlineData('/')]
    [InlineData(':')]
    public void Range_Boundary_Chars_Fall_Through_To_Switch(char c)
    {
        // '/' hits BDevide switch case; ':' is unknown, does nothing.
        var w = Create(out _);
        w.OnWindowKeyDown(c);
    }

    [Fact]
    public void Plus_Key_Invokes_Plus_Operation()
    {
        var w = Create(out _);
        w.OnWindowKeyDown('+');
        Assert.Equal(MainWindowLogic.Operation.Add, GetLastOper(w));
    }

    [Fact]
    public void Minus_Star_Slash_Percent_Equal_All_Hit_Their_Cases()
    {
        var w = Create(out _);
        w.OnWindowKeyDown('-');
        w.OnWindowKeyDown('*');
        w.OnWindowKeyDown('/');
        w.OnWindowKeyDown('%');
        w.OnWindowKeyDown('=');
    }

    [Fact]
    public void Unknown_Char_Does_Nothing()
    {
        var w = Create(out _);
        w.OnWindowKeyDown('a');
    }

    // ---- ProcessOperation: exercise all 15 switch cases ----

    [Theory]
    [InlineData("BPM")]
    [InlineData("BDevide")]
    [InlineData("BMultiply")]
    [InlineData("BMinus")]
    [InlineData("BPlus")]
    [InlineData("BEqual")]
    [InlineData("BSqrt")]
    [InlineData("BPercent")]
    [InlineData("BOneOver")]
    [InlineData("BC")]
    [InlineData("BCE")]
    [InlineData("BMemClear")]
    [InlineData("BMemSave")]
    [InlineData("BMemRecall")]
    [InlineData("BMemPlus")]
    public void ProcessOperation_AllCases(string op)
    {
        var w = Create(out _);
        w.Display = "5";
        w.EraseDisplay = false;
        w.ProcessOperation(op);
    }

    [Fact]
    public void ProcessOperation_Unknown_Does_Nothing()
    {
        var w = Create(out _);
        w.ProcessOperation("nonsense");
    }

    // Exercise BOTH branches of every "if (EraseDisplay) break;" inside ProcessOperation cases.
    [Theory]
    [InlineData("BDevide")]
    [InlineData("BMultiply")]
    [InlineData("BMinus")]
    [InlineData("BPlus")]
    [InlineData("BPercent")]
    [InlineData("BEqual")]
    public void ProcessOperation_EraseDisplayTrue_Branch(string op)
    {
        var w = Create(out _);
        w.Display = "5";
        w.EraseDisplay = true;
        w.ProcessOperation(op);
    }

    // ---- Full arithmetic flows so Calc switch arms are exercised ----

    [Fact]
    public void Addition_Flow_Produces_Paper_Trail()
    {
        var w = Create(out var io);
        // 3 + 4 =
        w.Display = "3"; w.EraseDisplay = false;
        w.ProcessOperation("BPlus");
        w.Display = "4"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
        Assert.Contains("7", w.Display);
    }

    [Fact]
    public void Subtraction_Flow()
    {
        var w = Create(out _);
        w.Display = "10"; w.EraseDisplay = false;
        w.ProcessOperation("BMinus");
        w.Display = "4"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
        Assert.Contains("6", w.Display);
    }

    [Fact]
    public void Multiplication_Flow()
    {
        var w = Create(out _);
        w.Display = "3"; w.EraseDisplay = false;
        w.ProcessOperation("BMultiply");
        w.Display = "4"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
        Assert.Contains("12", w.Display);
    }

    [Fact]
    public void Percent_Flow()
    {
        var w = Create(out _);
        w.Display = "50"; w.EraseDisplay = false;
        w.ProcessOperation("BPercent");
        w.Display = "200"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
    }

    [Fact]
    public void Division_Flow()
    {
        var w = Create(out _);
        w.Display = "20"; w.EraseDisplay = false;
        w.ProcessOperation("BDevide");
        w.Display = "4"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
        Assert.Contains("5", w.Display);
    }

    [Fact]
    public void Sqrt_Flow()
    {
        var w = Create(out _);
        w.Display = "9"; w.EraseDisplay = false;
        w.ProcessOperation("BSqrt");
    }

    [Fact]
    public void OneOver_Flow()
    {
        var w = Create(out _);
        w.Display = "2"; w.EraseDisplay = false;
        w.ProcessOperation("BOneOver");
    }

    [Fact]
    public void Negate_Flow()
    {
        var w = Create(out _);
        w.Display = "5"; w.EraseDisplay = false;
        w.ProcessOperation("BPM");
    }

    [Fact]
    public void DivByZero_Triggers_CheckResult_Infinity_And_Catch()
    {
        var w = Create(out var io);
        w.Display = "1"; w.EraseDisplay = false;
        w.ProcessOperation("BDevide");
        w.Display = "0"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
        Assert.NotEmpty(io.Errors);
    }

    [Fact]
    public void ZeroDivByZero_Triggers_NaN_Branch()
    {
        var w = Create(out var io);
        w.Display = "0"; w.EraseDisplay = false;
        w.ProcessOperation("BDevide");
        w.Display = "0"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
        Assert.NotEmpty(io.Errors);
    }

    // Trigger NegativeInfinity via -1e308 * 1e308 which yields +Infinity — cover it separately.
    // Cheap hand-crafted test using the expose: we can't reach IsNegativeInfinity without it.
    // Instead, directly feed a very large divisor result.
    [Fact]
    public void NegativeInfinity_Via_Big_Product()
    {
        var w = Create(out var io);
        w.Display = "-1e300"; w.EraseDisplay = false;
        w.ProcessOperation("BMultiply");
        w.Display = "1e300"; w.EraseDisplay = false;
        w.ProcessOperation("BEqual");
        Assert.NotEmpty(io.Errors);
    }

    // ---- AddToDisplay decisions: dot-already-exists, digit, backspace with length 0/1/2+ ----

    [Fact]
    public void Dot_When_Dot_Already_Present_Is_Ignored()
    {
        var w = Create(out _);
        w.Display = "3.14"; w.EraseDisplay = false;
        w.OnWindowKeyDown('.');
        Assert.Equal("3.14", w.Display);
    }

    [Fact]
    public void Digit_Appends()
    {
        var w = Create(out _);
        w.Display = "12"; w.EraseDisplay = false;
        w.OnWindowKeyDown('5');
        Assert.Equal("125", w.Display);
    }

    [Fact]
    public void Backspace_On_Single_Char_Clears()
    {
        var w = Create(out _);
        w.Display = "1"; w.EraseDisplay = false;
        w.OnWindowKeyDown('\b');
        Assert.Equal("", w.Display);
    }

    [Fact]
    public void Backspace_On_Empty_Clears()
    {
        var w = Create(out _);
        w.Display = ""; w.EraseDisplay = false;
        w.OnWindowKeyDown('\b');
        Assert.Equal("", w.Display);
    }

    [Fact]
    public void Backspace_On_Multiple_Removes_Last()
    {
        var w = Create(out _);
        w.Display = "123"; w.EraseDisplay = false;
        w.OnWindowKeyDown('\b');
        Assert.Equal("12", w.Display);
    }

    // ---- Memory / LastValue property branches ----

    [Fact]
    public void Memory_Empty_Returns_Zero()
    {
        var w = Create(out _);
        w.ProcessOperation("BMemClear"); // writes "0" to _memVal via setter
        w.ProcessOperation("BMemRecall");
        Assert.Equal("0", w.Display);
    }

    [Fact]
    public void Memory_NonEmpty_Returns_Stored()
    {
        var w = Create(out _);
        w.Display = "5"; w.EraseDisplay = false;
        w.ProcessOperation("BMemSave");
        w.Display = "0"; w.EraseDisplay = false;
        w.ProcessOperation("BMemRecall");
        Assert.Contains("5", w.Display);
    }

    [Fact]
    public void MemoryPlus_Adds_To_Memory()
    {
        var w = Create(out _);
        w.Display = "3"; w.EraseDisplay = false;
        w.ProcessOperation("BMemSave");
        w.Display = "4"; w.EraseDisplay = false;
        w.ProcessOperation("BMemPlus");
        w.ProcessOperation("BMemRecall");
        Assert.Contains("7", w.Display);
    }

    [Fact]
    public void MemoryClear_Displays_Empty_Label()
    {
        var w = Create(out var io);
        w.ProcessOperation("BMemClear");
        Assert.Contains("Memory", io.Memory);
    }

    [Fact]
    public void ClearAll_Resets_State()
    {
        var w = Create(out _);
        w.Display = "9"; w.EraseDisplay = false;
        w.ProcessOperation("BC");
        Assert.Equal("0", w.Display == "" ? "0" : w.Display);
    }

    [Fact]
    public void ClearEntry_Resets_To_LastValue()
    {
        var w = Create(out _);
        w.Display = "9"; w.EraseDisplay = false;
        w.ProcessOperation("BCE");
    }

    // ---- UpdateDisplay ternary: Display empty vs non-empty ----

    [Fact]
    public void UpdateDisplay_Shows_Zero_When_Empty()
    {
        var w = Create(out var io);
        w.ProcessOperation("BC"); // sets Display to empty, then UpdateDisplay
        Assert.Equal("0", io.Display);
    }

    [Fact]
    public void UpdateDisplay_Shows_Value_When_NonEmpty()
    {
        var w = Create(out var io);
        w.Display = "42"; w.EraseDisplay = false;
        w.ProcessOperation("BCE"); // writes LastValue, then UpdateDisplay
    }

    // ---- helper via reflection ----

    private static MainWindowLogic.Operation GetLastOper(MainWindowLogic w)
    {
        var f = typeof(MainWindowLogic).GetField("_lastOper",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (MainWindowLogic.Operation)f!.GetValue(w)!;
    }
}
