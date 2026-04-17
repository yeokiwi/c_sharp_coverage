namespace CalculatorDemo.Coverable;

public interface ICalculatorIo
{
    void SetDisplay(string text);
    void AppendPaper(string arguments, string result);
    void ClearPaper();
    void SetMemoryLabel(string text);
    void ShowError(string title, string message);
}
