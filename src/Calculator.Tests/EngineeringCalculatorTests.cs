// Licensed under the MIT License.

using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CalculatorApp.ViewModel;
using CalculatorApp.ViewModel.Common;

namespace Calculator.Tests
{
    [TestClass]
    public class EngineeringCalculatorTests
    {
        private static double Read(EngineeringField field) =>
            double.Parse(field.Text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture);

        // Electric power fields: 0 Voltage, 1 Current, 2 Power factor (defaults to 1), 3 Power
        // Ohm's law fields:      0 Voltage, 1 Current, 2 Resistance
        // Mechanical fields:     0 Torque (lb-ft), 1 Speed (RPM), 2 Power (hp)

        [TestMethod]
        public void OhmsLaw_AnyTwoValuesComputeTheThird()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.OhmsLaw);
            vm.Fields[1].Text = "2";      // I
            Assert.AreEqual(string.Empty, vm.Fields[0].Text, "nothing to calculate from one value");
            vm.Fields[2].Text = "6";      // R
            Assert.AreEqual(12.0, Read(vm.Fields[0]), 1e-9);
            Assert.IsTrue(vm.Fields[0].IsCalculated);
            Assert.IsFalse(vm.Fields[1].IsCalculated);
        }

        [TestMethod]
        public void OhmsLaw_TypingIntoTheResultRecalculatesTheOldestInput()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.OhmsLaw);
            vm.Fields[1].Text = "2";      // I (oldest)
            vm.Fields[2].Text = "6";      // R      -> U = 12
            vm.Fields[0].Text = "24";     // user overrides U -> I (least recently edited) is recalculated
            Assert.IsTrue(vm.Fields[1].IsCalculated);
            Assert.AreEqual(4.0, Read(vm.Fields[1]), 1e-9);
            Assert.AreEqual("6", vm.Fields[2].Text);
        }

        [TestMethod]
        public void OhmsLaw_UnitChangeOnInputReinterpretsValue()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.OhmsLaw);
            vm.Fields[0].Text = "12";     // U
            vm.Fields[2].Text = "6";      // R -> I = 2 A
            vm.Fields[2].SelectedUnitIndex = 2; // 6 kΩ
            Assert.AreEqual(0.002, Read(vm.Fields[1]), 1e-12);
        }

        [TestMethod]
        public void OhmsLaw_ClearingAnInputClearsTheResult()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.OhmsLaw);
            vm.Fields[1].Text = "2";
            vm.Fields[2].Text = "6";
            vm.Fields[2].Text = "";
            Assert.AreEqual(string.Empty, vm.Fields[0].Text);
            Assert.IsFalse(vm.Fields[0].IsCalculated);
        }

        [TestMethod]
        public void OhmsLaw_DivideByZeroShowsError()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.OhmsLaw);
            vm.Fields[0].Text = "12";
            vm.Fields[2].Text = "0";
            Assert.AreEqual(string.Empty, vm.Fields[1].Text);
            Assert.IsTrue(vm.HasError);
        }

        [TestMethod]
        public void ElectricPower_VoltsAndAmpsGiveKilowattsWithDefaultPowerFactor()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.ElectricPower);
            Assert.AreEqual("1", vm.Fields[2].Text);
            vm.Fields[0].Text = "240";
            vm.Fields[1].Text = "20";
            Assert.AreEqual(4.8, Read(vm.Fields[3]), 1e-9);
            vm.Fields[2].Text = "0.9";
            Assert.AreEqual(4.32, Read(vm.Fields[3]), 1e-9);
        }

        [TestMethod]
        public void ElectricPower_ThreePhaseAmpsFromKilowatts()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.ElectricPower);
            vm.SelectedOptionIndex = 1;   // three-phase
            vm.Fields[3].Text = "50";     // kW
            vm.Fields[0].Text = "480";    // V
            vm.Fields[2].Text = "0.85";   // PF  -> current is the only one not entered
            Assert.IsTrue(vm.Fields[1].IsCalculated);
            Assert.AreEqual(50000 / (Math.Sqrt(3) * 480 * 0.85), Read(vm.Fields[1]), 0.001);
        }

        [TestMethod]
        public void ElectricPower_RejectsPowerFactorAboveOne()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.ElectricPower);
            vm.Fields[0].Text = "240";
            vm.Fields[1].Text = "20";
            vm.Fields[2].Text = "1.5";
            Assert.AreEqual(string.Empty, vm.Fields[3].Text);
            Assert.IsTrue(vm.HasError);
        }

        [TestMethod]
        public void ElectricPower_ClearRestoresDefaultPowerFactor()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.ElectricPower);
            vm.Fields[0].Text = "240";
            vm.Fields[1].Text = "20";
            vm.ClearCommand.Execute(null);
            Assert.AreEqual(string.Empty, vm.Fields[0].Text);
            Assert.AreEqual(string.Empty, vm.Fields[3].Text);
            Assert.AreEqual("1", vm.Fields[2].Text);
        }

        [TestMethod]
        public void MechanicalPower_HorsepowerFromTorqueAndRpm()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.MechanicalPower);
            vm.Fields[0].Text = "5252";   // lb-ft
            vm.Fields[1].Text = "1000";   // RPM
            Assert.AreEqual(1000.0, Read(vm.Fields[2]), 0.5);
        }

        [TestMethod]
        public void MechanicalPower_TorqueFromKilowattsAndRpm()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.MechanicalPower);
            vm.Fields[0].SelectedUnitIndex = 1; // N·m
            vm.Fields[2].SelectedUnitIndex = 1; // kW
            vm.Fields[2].Text = "100";
            vm.Fields[1].Text = "3000";
            Assert.IsTrue(vm.Fields[0].IsCalculated);
            Assert.AreEqual(318.31, Read(vm.Fields[0]), 0.01);
        }

        [TestMethod]
        public void InvalidTextShowsError()
        {
            var vm = EngineeringCalculatorViewModel.Create(ViewMode.MechanicalPower);
            vm.Fields[0].Text = "abc";
            vm.Fields[1].Text = "1000";
            Assert.IsTrue(vm.HasError);
            Assert.AreEqual(string.Empty, vm.Fields[2].Text);
        }
    }
}
