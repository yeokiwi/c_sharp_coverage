// Extracted decision-heavy logic from CalculatorDemo/MainWindow.cs.
// Structure preserved line-by-line; WPF dependencies replaced with an ICalculatorIo seam.

using System;
using System.Globalization;

namespace CalculatorDemo.Coverable
{
    public sealed class MainWindowLogic
    {
        private readonly ICalculatorIo _io;
        private readonly PaperTrail _paper;
        private Operation _lastOper;
        private string _lastVal;
        private string _memVal;

        public MainWindowLogic(ICalculatorIo io)
        {
            _io = io;
            _paper = new PaperTrail(this);
            _lastVal = string.Empty;
            _memVal = string.Empty;
            Display = string.Empty;
            ProcessKey('0');
            EraseDisplay = true;
        }

        public bool EraseDisplay { get; set; }

        private double Memory
        {
            get
            {
                if (_memVal == string.Empty)
                    return 0.0;
                return Convert.ToDouble(_memVal);
            }
            set { _memVal = value.ToString(CultureInfo.InvariantCulture); }
        }

        private string LastValue
        {
            get
            {
                if (_lastVal == string.Empty)
                    return "0";
                return _lastVal;
            }
            set { _lastVal = value; }
        }

        public string Display { get; set; }

        public void OnWindowKeyDown(char c)
        {
            if ((c >= '0' && c <= '9') || c == '.' || c == '\b')
            {
                ProcessKey(c);
                return;
            }
            switch (c)
            {
                case '+':
                    ProcessOperation("BPlus");
                    break;
                case '-':
                    ProcessOperation("BMinus");
                    break;
                case '*':
                    ProcessOperation("BMultiply");
                    break;
                case '/':
                    ProcessOperation("BDevide");
                    break;
                case '%':
                    ProcessOperation("BPercent");
                    break;
                case '=':
                    ProcessOperation("BEqual");
                    break;
            }
        }

        public void ProcessKey(char c)
        {
            if (EraseDisplay)
            {
                Display = string.Empty;
                EraseDisplay = false;
            }
            AddToDisplay(c);
        }

        public void ProcessOperation(string s)
        {
            var d = 0.0;
            switch (s)
            {
                case "BPM":
                    _lastOper = Operation.Negate;
                    LastValue = Display;
                    CalcResults();
                    LastValue = Display;
                    EraseDisplay = true;
                    _lastOper = Operation.None;
                    break;
                case "BDevide":
                    if (EraseDisplay)
                    {
                        _lastOper = Operation.Devide;
                        break;
                    }
                    CalcResults();
                    _lastOper = Operation.Devide;
                    LastValue = Display;
                    EraseDisplay = true;
                    break;
                case "BMultiply":
                    if (EraseDisplay)
                    {
                        _lastOper = Operation.Multiply;
                        break;
                    }
                    CalcResults();
                    _lastOper = Operation.Multiply;
                    LastValue = Display;
                    EraseDisplay = true;
                    break;
                case "BMinus":
                    if (EraseDisplay)
                    {
                        _lastOper = Operation.Subtract;
                        break;
                    }
                    CalcResults();
                    _lastOper = Operation.Subtract;
                    LastValue = Display;
                    EraseDisplay = true;
                    break;
                case "BPlus":
                    if (EraseDisplay)
                    {
                        _lastOper = Operation.Add;
                        break;
                    }
                    CalcResults();
                    _lastOper = Operation.Add;
                    LastValue = Display;
                    EraseDisplay = true;
                    break;
                case "BEqual":
                    if (EraseDisplay)
                        break;
                    CalcResults();
                    EraseDisplay = true;
                    _lastOper = Operation.None;
                    LastValue = Display;
                    break;
                case "BSqrt":
                    _lastOper = Operation.Sqrt;
                    LastValue = Display;
                    CalcResults();
                    LastValue = Display;
                    EraseDisplay = true;
                    _lastOper = Operation.None;
                    break;
                case "BPercent":
                    if (EraseDisplay)
                    {
                        _lastOper = Operation.Percent;
                        break;
                    }
                    CalcResults();
                    _lastOper = Operation.Percent;
                    LastValue = Display;
                    EraseDisplay = true;
                    break;
                case "BOneOver":
                    _lastOper = Operation.OneX;
                    LastValue = Display;
                    CalcResults();
                    LastValue = Display;
                    EraseDisplay = true;
                    _lastOper = Operation.None;
                    break;
                case "BC":
                    _lastOper = Operation.None;
                    Display = LastValue = string.Empty;
                    _paper.Clear();
                    UpdateDisplay();
                    break;
                case "BCE":
                    _lastOper = Operation.None;
                    Display = LastValue;
                    UpdateDisplay();
                    break;
                case "BMemClear":
                    Memory = 0.0F;
                    DisplayMemory();
                    break;
                case "BMemSave":
                    Memory = Convert.ToDouble(Display);
                    DisplayMemory();
                    EraseDisplay = true;
                    break;
                case "BMemRecall":
                    Display = Memory.ToString(CultureInfo.InvariantCulture);
                    UpdateDisplay();
                    EraseDisplay = false;
                    break;
                case "BMemPlus":
                    d = Memory + Convert.ToDouble(Display);
                    Memory = d;
                    DisplayMemory();
                    EraseDisplay = true;
                    break;
            }
        }

        private double Calc(Operation lastOper)
        {
            var d = 0.0;
            try
            {
                switch (lastOper)
                {
                    case Operation.Devide:
                        _paper.AddArguments(LastValue + " / " + Display);
                        d = (Convert.ToDouble(LastValue) / Convert.ToDouble(Display));
                        CheckResult(d);
                        _paper.AddResult(d.ToString(CultureInfo.InvariantCulture));
                        break;
                    case Operation.Add:
                        _paper.AddArguments(LastValue + " + " + Display);
                        d = Convert.ToDouble(LastValue) + Convert.ToDouble(Display);
                        CheckResult(d);
                        _paper.AddResult(d.ToString(CultureInfo.InvariantCulture));
                        break;
                    case Operation.Multiply:
                        _paper.AddArguments(LastValue + " * " + Display);
                        d = Convert.ToDouble(LastValue) * Convert.ToDouble(Display);
                        CheckResult(d);
                        _paper.AddResult(d.ToString(CultureInfo.InvariantCulture));
                        break;
                    case Operation.Percent:
                        _paper.AddArguments(LastValue + " % " + Display);
                        d = (Convert.ToDouble(LastValue) * Convert.ToDouble(Display)) / 100.0F;
                        CheckResult(d);
                        _paper.AddResult(d.ToString(CultureInfo.InvariantCulture));
                        break;
                    case Operation.Subtract:
                        _paper.AddArguments(LastValue + " - " + Display);
                        d = Convert.ToDouble(LastValue) - Convert.ToDouble(Display);
                        CheckResult(d);
                        _paper.AddResult(d.ToString(CultureInfo.InvariantCulture));
                        break;
                    case Operation.Sqrt:
                        _paper.AddArguments("Sqrt( " + LastValue + " )");
                        d = Math.Sqrt(Convert.ToDouble(LastValue));
                        CheckResult(d);
                        _paper.AddResult(d.ToString(CultureInfo.InvariantCulture));
                        break;
                    case Operation.OneX:
                        _paper.AddArguments("1 / " + LastValue);
                        d = 1.0F / Convert.ToDouble(LastValue);
                        CheckResult(d);
                        _paper.AddResult(d.ToString(CultureInfo.InvariantCulture));
                        break;
                    case Operation.Negate:
                        d = Convert.ToDouble(LastValue) * (-1.0F);
                        break;
                }
            }
            catch
            {
                d = 0;
                _paper.AddResult("Error");
                _io.ShowError("Calculator", "Operation cannot be performed");
            }
            return d;
        }

        private void CheckResult(double d)
        {
            if (double.IsNegativeInfinity(d) || double.IsPositiveInfinity(d) || double.IsNaN(d))
                throw new Exception("Illegal value");
        }

        private void DisplayMemory()
        {
            if (_memVal != string.Empty)
                _io.SetMemoryLabel("Memory: " + _memVal);
            else
                _io.SetMemoryLabel("Memory: [empty]");
        }

        private void CalcResults()
        {
            double d;
            if (_lastOper == Operation.None)
                return;
            d = Calc(_lastOper);
            Display = d.ToString(CultureInfo.InvariantCulture);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            _io.SetDisplay(Display == string.Empty ? "0" : Display);
        }

        private void AddToDisplay(char c)
        {
            if (c == '.')
            {
                if (Display.IndexOf('.', 0) >= 0)
                    return;
                Display = Display + c;
            }
            else
            {
                if (c >= '0' && c <= '9')
                {
                    Display = Display + c;
                }
                else if (c == '\b')
                {
                    if (Display.Length <= 1)
                        Display = string.Empty;
                    else
                    {
                        var i = Display.Length;
                        Display = Display.Remove(i - 1, 1);
                    }
                }
            }
            UpdateDisplay();
        }

        public enum Operation
        {
            None, Devide, Multiply, Subtract, Add, Percent, Sqrt, OneX, Negate
        }

        private sealed class PaperTrail
        {
            private readonly MainWindowLogic _owner;
            private string _args;

            public PaperTrail(MainWindowLogic owner)
            {
                _owner = owner;
                _args = string.Empty;
            }

            public void AddArguments(string a) { _args = a; }
            public void AddResult(string r) { _owner._io.AppendPaper(_args, r); }
            public void Clear()
            {
                _owner._io.ClearPaper();
                _args = string.Empty;
            }
        }
    }
}
