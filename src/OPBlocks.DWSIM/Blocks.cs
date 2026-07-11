using DWSIM.Interfaces.Enums;
using OPBlocks.Core;

namespace OPBlocks.DWSIM
{
    // ===================================================================
    //  DWSIM-native registrations for all 25 ONE PROCESS blocks.
    //  Each class is a thin shell: physics, ports, parameters and validation
    //  all come from the wrapped CAPE-OPEN block (zero duplication).
    //  DWSIM's unitops scanner instantiates every exported class below.
    // ===================================================================

    // ---- Family A — Thermal Desalination & Evaporation ----

    public sealed class EvapPondDW : DwsimUnitAdapter
    {
        public EvapPondDW() : base("EVP-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.EvapPond(); }
    }

    public sealed class MembraneDistillationDW : DwsimUnitAdapter
    {
        public MembraneDistillationDW() : base("MD-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.MembraneDistillation(); }
    }

    public sealed class MultiEffectDistillationDW : DwsimUnitAdapter
    {
        public MultiEffectDistillationDW() : base("MED-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.MultiEffectDistillation(); }
    }

    public sealed class MultiStageFlashDW : DwsimUnitAdapter
    {
        public MultiStageFlashDW() : base("MSF-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.MultiStageFlash(); }
    }

    public sealed class MechanicalVaporCompressionDW : DwsimUnitAdapter
    {
        public MechanicalVaporCompressionDW() : base("MVC-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.MechanicalVaporCompression(); }
    }

    // ---- Family B — Membrane Separations ----

    public sealed class ReverseOsmosisDW : DwsimUnitAdapter
    {
        public ReverseOsmosisDW() : base("RO-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.ReverseOsmosis(); }
    }

    public sealed class NanofiltrationDW : DwsimUnitAdapter
    {
        public NanofiltrationDW() : base("NF-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.Nanofiltration(); }
    }

    public sealed class UltrafiltrationDW : DwsimUnitAdapter
    {
        public UltrafiltrationDW() : base("UF-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.Ultrafiltration(); }
    }

    public sealed class ForwardOsmosisDW : DwsimUnitAdapter
    {
        public ForwardOsmosisDW() : base("FO-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.ForwardOsmosis(); }
    }

    public sealed class PressureRetardedOsmosisDW : DwsimUnitAdapter
    {
        public PressureRetardedOsmosisDW() : base("PRO-", SimulationObjectClass.CleanPowerSources) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Desalination.PressureRetardedOsmosis(); }
    }

    // ---- Family C — Electrochemical Water Treatment ----

    public sealed class ElectrodialysisDW : DwsimUnitAdapter
    {
        public ElectrodialysisDW() : base("ED-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Electro.Electrodialysis(); }
    }

    public sealed class ElectrodeionizationDW : DwsimUnitAdapter
    {
        public ElectrodeionizationDW() : base("EDI-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Electro.Electrodeionization(); }
    }

    public sealed class CapacitiveDeionizationDW : DwsimUnitAdapter
    {
        public CapacitiveDeionizationDW() : base("CDI-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Electro.CapacitiveDeionization(); }
    }

    public sealed class ChlorAlkaliDW : DwsimUnitAdapter
    {
        public ChlorAlkaliDW() : base("CA-", SimulationObjectClass.Electrolyzers) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Electro.ChlorAlkali(); }
    }

    public sealed class IonExchangeDW : DwsimUnitAdapter
    {
        public IonExchangeDW() : base("IX-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Electro.IonExchange(); }
    }

    // ---- Family D — Lithium & Sorption ----

    public sealed class DirectLithiumExtractionDW : DwsimUnitAdapter
    {
        public DirectLithiumExtractionDW() : base("DLE-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Lithium.DirectLithiumExtraction(); }
    }

    public sealed class SolventExtractionDW : DwsimUnitAdapter
    {
        public SolventExtractionDW() : base("SX-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Lithium.SolventExtraction(); }
    }

    public sealed class ActivatedCarbonDW : DwsimUnitAdapter
    {
        public ActivatedCarbonDW() : base("GAC-", SimulationObjectClass.Separators) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Lithium.ActivatedCarbon(); }
    }

    public sealed class CrystallizerDW : DwsimUnitAdapter
    {
        public CrystallizerDW() : base("CRY-", SimulationObjectClass.Solids) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Lithium.Crystallizer(); }
    }

    public sealed class ChemicalPrecipitationDW : DwsimUnitAdapter
    {
        public ChemicalPrecipitationDW() : base("PPT-", SimulationObjectClass.Reactors) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Lithium.ChemicalPrecipitation(); }
    }

    // ---- Family E — Energy & Gas Processing ----

    public sealed class PemElectrolyzerDW : DwsimUnitAdapter
    {
        public PemElectrolyzerDW() : base("PEM-", SimulationObjectClass.Electrolyzers) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Energy.PemElectrolyzer(); }
    }

    public sealed class AlkalineElectrolyzerDW : DwsimUnitAdapter
    {
        public AlkalineElectrolyzerDW() : base("AEL-", SimulationObjectClass.Electrolyzers) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Energy.AlkalineElectrolyzer(); }
    }

    public sealed class FuelCellDW : DwsimUnitAdapter
    {
        public FuelCellDW() : base("FC-", SimulationObjectClass.CleanPowerSources) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Energy.FuelCell(); }
    }

    public sealed class RotatingPackedBedDW : DwsimUnitAdapter
    {
        public RotatingPackedBedDW() : base("RPB-", SimulationObjectClass.Columns) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Energy.RotatingPackedBed(); }
    }

    public sealed class UvAopReactorDW : DwsimUnitAdapter
    {
        public UvAopReactorDW() : base("UV-", SimulationObjectClass.Reactors) { }
        protected override UnitBase CreateBlock() { return new global::OPBlocks.Energy.UvAopReactor(); }
    }
}
