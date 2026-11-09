// Licensed under the MIT License.

// Multi-field calculators listed after Date calculation: Electric power, Ohm's law, Mechanical power.
//
// Each calculator has N related fields. Fill in any N-1 of them and the remaining one is calculated
// as you type. When every field has a value, the field you edited least recently is the one that gets
// recalculated, so you can type into any field (including the result) and the others follow.
// Every field may offer several units; values are converted to a base unit before solving.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using CalculatorApp.ViewModel.Common;

namespace CalculatorApp.ViewModel
{
    /// <summary>A unit a field can be shown in. Value in base unit = shown value × Factor.</summary>
    public sealed class EngineeringUnit
    {
        public EngineeringUnit(string name, double factor)
        {
            Name = name;
            Factor = factor;
        }

        public string Name { get; }
        public double Factor { get; }

        public override string ToString() => Name;
    }

    [Windows.UI.Xaml.Data.Bindable]
    public sealed partial class EngineeringField : ObservableObject
    {
        private readonly List<EngineeringUnit> _units;
        private string _text = string.Empty;
        private int _selectedUnitIndex;
        private bool _isCalculated;
        private bool _settingResult;

        internal EngineeringField(string label, params EngineeringUnit[] units)
        {
            Label = label;
            _units = units.ToList();
            UnitNames = _units.Select(u => u.Name).ToList();
        }

        /// <summary>Raised when the user (not the calculator) changes the text.</summary>
        internal event EventHandler UserEdited;

        /// <summary>Raised when the selected unit changes.</summary>
        internal event EventHandler UnitChanged;

        public string Label { get; }

        public IList<string> UnitNames { get; }

        public bool HasUnitChoice => _units.Count > 1;

        public string SingleUnitName => _units.Count == 1 ? _units[0].Name : string.Empty;

        public string Text
        {
            get => _text;
            set
            {
                if (SetProperty(ref _text, value ?? string.Empty) && !_settingResult)
                {
                    UserEdited?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int SelectedUnitIndex
        {
            get => _selectedUnitIndex;
            set
            {
                if (value < 0 || value >= _units.Count)
                {
                    return;
                }
                if (SetProperty(ref _selectedUnitIndex, value))
                {
                    UnitChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>True when the value shown is calculated from the other fields.</summary>
        public bool IsCalculated
        {
            get => _isCalculated;
            internal set => SetProperty(ref _isCalculated, value);
        }

        internal bool IsBlank => string.IsNullOrWhiteSpace(_text);

        internal double UnitFactor => _units[_selectedUnitIndex].Factor;

        /// <summary>The value in the base unit, or null if the text is empty or not a number.</summary>
        internal double? BaseValue
        {
            get
            {
                double? shown = EngineeringFormat.Parse(_text);
                return shown.HasValue ? shown.Value * UnitFactor : (double?)null;
            }
        }

        /// <summary>Shows a calculated value (given in the base unit) in the selected unit.</summary>
        internal void SetResult(double? baseValue)
        {
            _settingResult = true;
            try
            {
                Text = baseValue.HasValue ? EngineeringFormat.Format(baseValue.Value / UnitFactor) : string.Empty;
            }
            finally
            {
                _settingResult = false;
            }
        }
    }

    /// <summary>What a solver returns.</summary>
    internal sealed class EngineeringSolution
    {
        public double? Value;
        public string Error;
        public List<string> Notes = new List<string>();
    }

    /// <param name="values">Base-unit values of every field. The target's entry is null.</param>
    /// <param name="target">Index of the field to calculate.</param>
    /// <param name="option">Selected option index (for example single- or three-phase), 0 if there are none.</param>
    internal delegate EngineeringSolution EngineeringSolver(double?[] values, int target, int option);

    [Windows.UI.Xaml.Data.Bindable]
    public sealed partial class EngineeringCalculatorViewModel : ObservableObject
    {
        private readonly EngineeringSolver _solver;
        private readonly IDictionary<int, double> _defaults; // field index -> default value in base units

        // Fields the user has filled in, least recently edited first. The calculated field is never in here.
        private readonly List<int> _editOrder = new List<int>();
        private int _calculatedIndex = -1;
        private int _selectedOptionIndex;
        private string _notesText = string.Empty;
        private string _errorText = string.Empty;

        private EngineeringCalculatorViewModel(
            ViewMode mode,
            IList<EngineeringField> fields,
            EngineeringSolver solver,
            IDictionary<int, double> defaults = null,
            string optionsLabel = null,
            IList<string> options = null)
        {
            Mode = mode;
            Fields = fields;
            _solver = solver;
            _defaults = defaults ?? new Dictionary<int, double>();
            OptionsLabel = optionsLabel ?? string.Empty;
            Options = options ?? new List<string>();

            ApplyDefaults();

            for (int i = 0; i < fields.Count; i++)
            {
                int index = i;
                fields[i].UserEdited += (_, __) => OnUserEdited(index);
                fields[i].UnitChanged += (_, __) => Recalculate();
            }
        }

        public ViewMode Mode { get; }
        public IList<EngineeringField> Fields { get; }
        public string OptionsLabel { get; }
        public IList<string> Options { get; }
        public bool HasOptions => Options.Count > 0;

        public int SelectedOptionIndex
        {
            get => _selectedOptionIndex;
            set
            {
                if (value < 0 || (Options.Count > 0 && value >= Options.Count))
                {
                    return;
                }
                if (SetProperty(ref _selectedOptionIndex, value))
                {
                    Recalculate();
                }
            }
        }

        public string NotesText
        {
            get => _notesText;
            private set
            {
                if (SetProperty(ref _notesText, value))
                {
                    OnPropertyChanged(nameof(HasNotes));
                }
            }
        }

        public bool HasNotes => !string.IsNullOrEmpty(_notesText);

        public string ErrorText
        {
            get => _errorText;
            private set
            {
                if (SetProperty(ref _errorText, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorText);

        [RelayCommand]
        private void Clear()
        {
            _editOrder.Clear();
            SetCalculated(-1);
            foreach (var field in Fields)
            {
                field.SetResult(null);
            }
            ApplyDefaults();
            NotesText = string.Empty;
            ErrorText = string.Empty;
        }

        /// <summary>Pre-fills default inputs (for example power factor 1) as the oldest "user" entries.</summary>
        private void ApplyDefaults()
        {
            foreach (var kvp in _defaults)
            {
                Fields[kvp.Key].SetResult(kvp.Value);
                _editOrder.Add(kvp.Key);
            }
        }

        private void OnUserEdited(int index)
        {
            _editOrder.Remove(index);

            if (Fields[index].IsBlank)
            {
                if (index == _calculatedIndex)
                {
                    // The user cleared the calculated value (probably to type a new one); don't refill it.
                    SetCalculated(-1);
                    NotesText = string.Empty;
                    ErrorText = string.Empty;
                    return;
                }
            }
            else
            {
                _editOrder.Add(index);
                if (index == _calculatedIndex)
                {
                    SetCalculated(-1);
                }
            }

            Recalculate();
        }

        private void SetCalculated(int index)
        {
            _calculatedIndex = index;
            for (int i = 0; i < Fields.Count; i++)
            {
                Fields[i].IsCalculated = i == index;
            }
        }

        internal void Recalculate()
        {
            int needed = Fields.Count - 1;

            // Any filled-in field that isn't a number is an error, whatever gets calculated.
            if (_editOrder.Any(i => !Fields[i].BaseValue.HasValue))
            {
                ClearCalculatedField();
                NotesText = string.Empty;
                ErrorText = "Enter numbers only.";
                return;
            }

            if (_editOrder.Count < needed)
            {
                ClearCalculatedField();
                NotesText = string.Empty;
                ErrorText = string.Empty;
                return;
            }

            // The most recently edited N-1 fields are the inputs; the other one is calculated.
            var inputs = _editOrder.Skip(_editOrder.Count - needed).ToList();
            int target = Enumerable.Range(0, Fields.Count).First(i => !inputs.Contains(i));
            _editOrder.Remove(target);
            SetCalculated(target);

            var values = new double?[Fields.Count];
            foreach (int i in inputs)
            {
                values[i] = Fields[i].BaseValue;
            }

            var solution = _solver(values, target, _selectedOptionIndex);
            if (solution.Value.HasValue && (double.IsNaN(solution.Value.Value) || double.IsInfinity(solution.Value.Value)))
            {
                solution.Value = null;
                solution.Error ??= "Can't calculate with these values.";
            }

            Fields[target].SetResult(solution.Value);
            ErrorText = solution.Error ?? string.Empty;
            NotesText = solution.Value.HasValue ? string.Join(Environment.NewLine, solution.Notes) : string.Empty;
        }

        private void ClearCalculatedField()
        {
            if (_calculatedIndex >= 0)
            {
                Fields[_calculatedIndex].SetResult(null);
                SetCalculated(-1);
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Calculators
        // ---------------------------------------------------------------------------------------------

        public static EngineeringCalculatorViewModel Create(ViewMode mode)
        {
            switch (mode)
            {
                case ViewMode.ElectricPower: return CreateElectricPower();
                case ViewMode.OhmsLaw: return CreateOhmsLaw();
                case ViewMode.MechanicalPower: return CreateMechanicalPower();
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a multi-field calculator mode.");
            }
        }

        // ---- Unit factors (to SI base units) ----
        private const double NewtonMetersPerPoundFoot = 1.3558179483314004;
        private const double NewtonMetersPerPoundInch = 0.1129848290276167;
        private const double NewtonMetersPerKilogramForceMeter = 9.80665;
        private const double WattsPerMechanicalHorsepower = 745.69987158227022;
        private const double WattsPerMetricHorsepower = 735.49875;
        private const double RadiansPerSecondPerRpm = 2 * Math.PI / 60;
        private static readonly double SqrtThree = Math.Sqrt(3);

        // ---- Electric power: P = k × V × I × PF  (k = 1 single-phase/DC, √3 three-phase) ----
        private static EngineeringCalculatorViewModel CreateElectricPower()
        {
            const int Volts = 0, Amps = 1, PF = 2, Power = 3;

            var fields = new List<EngineeringField>
            {
                new EngineeringField("Voltage",
                    new EngineeringUnit("V", 1),
                    new EngineeringUnit("kV", 1000)),
                new EngineeringField("Current",
                    new EngineeringUnit("A", 1),
                    new EngineeringUnit("mA", 0.001)),
                new EngineeringField("Power factor",
                    new EngineeringUnit("0 – 1", 1),
                    new EngineeringUnit("%", 0.01)),
                new EngineeringField("Power",
                    new EngineeringUnit("kW", 1000),
                    new EngineeringUnit("W", 1),
                    new EngineeringUnit("hp", WattsPerMechanicalHorsepower)),
            };

            EngineeringSolution Solve(double?[] v, int target, int option)
            {
                var s = new EngineeringSolution();
                double k = option == 1 ? SqrtThree : 1;

                if (target != PF && (v[PF].Value <= 0 || v[PF].Value > 1))
                {
                    s.Error = "Power factor must be more than 0 and no more than 1.";
                    return s;
                }

                switch (target)
                {
                    case Power:
                        s.Value = k * v[Volts].Value * v[Amps].Value * v[PF].Value;
                        break;
                    case Volts:
                        if (v[Amps].Value == 0) { s.Error = "Current can't be zero."; return s; }
                        s.Value = v[Power].Value / (k * v[Amps].Value * v[PF].Value);
                        break;
                    case Amps:
                        if (v[Volts].Value == 0) { s.Error = "Voltage can't be zero."; return s; }
                        s.Value = v[Power].Value / (k * v[Volts].Value * v[PF].Value);
                        break;
                    case PF:
                        if (v[Volts].Value == 0 || v[Amps].Value == 0) { s.Error = "Voltage and current can't be zero."; return s; }
                        s.Value = v[Power].Value / (k * v[Volts].Value * v[Amps].Value);
                        if (s.Value > 1 || s.Value < 0)
                        {
                            s.Error = "Power factor works out above 1. Check the values.";
                        }
                        break;
                }

                double p = target == Power ? s.Value.Value : v[Power].Value;
                double volts = target == Volts ? s.Value.Value : v[Volts].Value;
                double amps = target == Amps ? s.Value.Value : v[Amps].Value;
                double apparent = k * volts * amps;
                s.Notes.Add($"{EngineeringFormat.Format(apparent / 1000)} kVA apparent");
                if (Math.Abs(p) <= Math.Abs(apparent))
                {
                    s.Notes.Add($"{EngineeringFormat.Format(Math.Sqrt(apparent * apparent - p * p) / 1000)} kVAR reactive");
                }
                return s;
            }

            return new EngineeringCalculatorViewModel(
                ViewMode.ElectricPower,
                fields,
                Solve,
                defaults: new Dictionary<int, double> { [PF] = 1.0 },
                optionsLabel: "Phase",
                options: new List<string> { "Single-phase / DC", "Three-phase" });
        }

        // ---- Ohm's law: U = I × R ----
        private static EngineeringCalculatorViewModel CreateOhmsLaw()
        {
            const int Voltage = 0, Current = 1, Resistance = 2;

            var fields = new List<EngineeringField>
            {
                new EngineeringField("Voltage (U)",
                    new EngineeringUnit("V", 1),
                    new EngineeringUnit("mV", 0.001),
                    new EngineeringUnit("kV", 1000)),
                new EngineeringField("Current (I)",
                    new EngineeringUnit("A", 1),
                    new EngineeringUnit("mA", 0.001),
                    new EngineeringUnit("µA", 0.000001)),
                new EngineeringField("Resistance (R)",
                    new EngineeringUnit("Ω", 1),
                    new EngineeringUnit("mΩ", 0.001),
                    new EngineeringUnit("kΩ", 1000),
                    new EngineeringUnit("MΩ", 1000000)),
            };

            EngineeringSolution Solve(double?[] v, int target, int option)
            {
                var s = new EngineeringSolution();
                switch (target)
                {
                    case Voltage:
                        s.Value = v[Current].Value * v[Resistance].Value;
                        break;
                    case Current:
                        if (v[Resistance].Value == 0) { s.Error = "Resistance can't be zero."; return s; }
                        s.Value = v[Voltage].Value / v[Resistance].Value;
                        break;
                    case Resistance:
                        if (v[Current].Value == 0) { s.Error = "Current can't be zero."; return s; }
                        s.Value = v[Voltage].Value / v[Current].Value;
                        break;
                }

                double u = target == Voltage ? s.Value.Value : v[Voltage].Value;
                double i = target == Current ? s.Value.Value : v[Current].Value;
                s.Notes.Add($"{EngineeringFormat.Format(u * i)} W dissipated");
                return s;
            }

            return new EngineeringCalculatorViewModel(ViewMode.OhmsLaw, fields, Solve);
        }

        // ---- Mechanical power: P = T × ω ----
        private static EngineeringCalculatorViewModel CreateMechanicalPower()
        {
            const int Torque = 0, Speed = 1, Power = 2;

            var fields = new List<EngineeringField>
            {
                new EngineeringField("Torque",
                    new EngineeringUnit("lb-ft", NewtonMetersPerPoundFoot),
                    new EngineeringUnit("N·m", 1),
                    new EngineeringUnit("lb-in", NewtonMetersPerPoundInch),
                    new EngineeringUnit("kg·m", NewtonMetersPerKilogramForceMeter)),
                new EngineeringField("Speed",
                    new EngineeringUnit("RPM", RadiansPerSecondPerRpm),
                    new EngineeringUnit("rad/s", 1)),
                new EngineeringField("Power",
                    new EngineeringUnit("hp", WattsPerMechanicalHorsepower),
                    new EngineeringUnit("kW", 1000),
                    new EngineeringUnit("W", 1),
                    new EngineeringUnit("PS", WattsPerMetricHorsepower)),
            };

            EngineeringSolution Solve(double?[] v, int target, int option)
            {
                var s = new EngineeringSolution();
                switch (target)
                {
                    case Power:
                        s.Value = v[Torque].Value * v[Speed].Value;
                        break;
                    case Torque:
                        if (v[Speed].Value == 0) { s.Error = "Speed can't be zero."; return s; }
                        s.Value = v[Power].Value / v[Speed].Value;
                        break;
                    case Speed:
                        if (v[Torque].Value == 0) { s.Error = "Torque can't be zero."; return s; }
                        s.Value = v[Power].Value / v[Torque].Value;
                        break;
                }

                double power = target == Power ? s.Value.Value : v[Power].Value;
                s.Notes.Add($"{EngineeringFormat.Format(power / WattsPerMechanicalHorsepower)} hp  =  {EngineeringFormat.Format(power / 1000)} kW");
                return s;
            }

            return new EngineeringCalculatorViewModel(ViewMode.MechanicalPower, fields, Solve);
        }
    }

    internal static class EngineeringFormat
    {
        private const int SignificantDigits = 6;

        public static double? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            string trimmed = text.Trim();
            if (double.TryParse(trimmed, styles, CultureInfo.CurrentCulture, out double value) ||
                double.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out value))
            {
                return double.IsNaN(value) || double.IsInfinity(value) ? (double?)null : value;
            }
            return null;
        }

        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return string.Empty;
            }
            if (value == 0)
            {
                return "0";
            }

            double abs = Math.Abs(value);
            if (abs >= 1e12 || abs < 1e-6)
            {
                return value.ToString("G" + SignificantDigits, CultureInfo.CurrentCulture);
            }

            int decimals = SignificantDigits - 1 - (int)Math.Floor(Math.Log10(abs));
            decimals = Math.Max(0, Math.Min(15, decimals));
            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            return rounded.ToString("#,0.###############", CultureInfo.CurrentCulture);
        }
    }
}
