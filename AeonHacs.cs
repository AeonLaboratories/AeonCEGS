using HACS.Components;
using HACS.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Utilities;
using static HACS.Components.CegsPreferences;

namespace AeonCegs
{
	public class AeonHacs : HACS.Components.CEGS
	{
		#region HacsComponent

		protected virtual void PreConnect()
		{
			SampleLog = Find<HacsLog>("SampleLog");
			AmbientLog = Find<DataLog>("AmbientLog");

			// These components are needed to allow the inclusion of
			// non-INamedValue properties of theirs in logged data.
			HeaterController1 = Find<HC6ControllerB2>("HeaterController1");
			HeaterController2 = Find<HC6ControllerB2>("HeaterController2");
			HeaterController3 = Find<HC6ControllerB2>("HeaterController3");

			#region Logs

			AmbientLog.AddNewValue("HC1.CJ0", -1, "0.0",
				() => HeaterController1.ColdJunction0Temperature);
			AmbientLog.AddNewValue("HC1.CJ1", -1, "0.0",
				() => HeaterController1.ColdJunction1Temperature);

			AmbientLog.AddNewValue("HC2.CJ0", -1, "0.0",
				() => HeaterController2.ColdJunction0Temperature);
			AmbientLog.AddNewValue("HC2.CJ1", -1, "0.0",
				() => HeaterController2.ColdJunction1Temperature);

			AmbientLog.AddNewValue("HC3.CJ0", -1, "0.0",
				() => HeaterController3.ColdJunction0Temperature);
			AmbientLog.AddNewValue("HC3.CJ1", -1, "0.0",
				() => HeaterController3.ColdJunction1Temperature);

			AmbientLog.AddNewValue("tAmbientRoC", -1, "0.0",
				() => Ambient.Thermometer.RateOfChange * 60);

			VMPressureLog = Find<DataLog>("VMPressureLog");
			VMPressureLog.Changed = (col) => col.Source is DualManometer dm ?
				(col.PriorValue is double p ?
					DualManometer.SignificantChange(p, dm.Value) :
					true) :
				false;
			#endregion Logs
		}

		protected override void Connect()
		{
			base.Connect();

			#region a CEGS needs these
			// The base CEGS really can't do "carbon extraction and graphitization"
			// unless these objects are defined.

			Power = Find<Power>("Power");
			Ambient = Find<Chamber>("Ambient");
			VacuumSystem = Find<VacuumSystem>("VacuumSystem");

			IM = Find<Section>("IM");
			VTT = Find<Section>("VTT");
			MC = Find<Section>("MC");
			Split = Find<Section>("Split");
			GM = Find<Section>("GM");

			VTT_MC = Find<Section>("VTT_MC");
			MC_Split = Find<Section>("MC_Split");

			ugCinMC = Find<Meter>("ugCinMC");

			InletPorts = CachedList<IInletPort>();
			GraphiteReactors = CachedList<IGraphiteReactor>();
			d13CPorts = CachedList<Id13CPort>();

			#endregion a CEGS needs these

			#region CEGS options
			// The CEGS doesn't require these, but provides
			// added functionality if they are present.

			d13C = Find<Section>("d13C");

			// Note: By default d13CPort is heuristically selected
			// based on the presence and/or absence of specifically named
			// sections. Override that Property if the default getter
			// doesn't work for this system.
			#endregion CEGS options

			VTT.Clean = () => Clean(VTT);

			VP = Find<d13CPort>("VP");

			pBakeout = Find<Voltmeter>("pBakeout");
			vPneumatic = Find<PneumaticValve>(nameof(vPneumatic));  // TODO: doing this is potentially problematic
		}

		protected override void PostConnect()
		{
			// This section is used to approximate the expansion volume
			// containing both the d13C and AMS fractions of the sample.
			// It provides a representative volume 
			// for calculating the 13C/14C volume ratio
			d13C_14C = Section.Combine(Find<Section>("MC_GM"), d13C);

			// TODO: Section.Combine creates an unnamed section. This shouldn't be necessary.
			d13C_14C.Name = null;   // don't save it in settings file

			base.PostConnect();
		}

		#endregion HacsComponent

		#region System Configuration
		#region HacsComponents

		public d13CPort VP;

		public Voltmeter pBakeout;

		public PneumaticValve vPneumatic;

		public HC6ControllerB2 HeaterController1;
		public HC6ControllerB2 HeaterController2;
		public HC6ControllerB2 HeaterController3;

		#endregion HacsComponents
		#endregion System Configuration

		#region Periodic system activities & maintenance

		protected override void ZeroPressureGauges()
		{
			base.ZeroPressureGauges();

			// ensure baseline VM pressure & steady state
			if (VacuumSystem.TimeAtBaseline.TotalSeconds < 10)
				return;

			// Calibrate this zero manually, with the Turbo Pump 
			// evacuating the Foreline.
			// Write VacuumSystem code to do this (only possible if vR is present).
			// ZeroIfNeeded(pForeline, 20);

			if (MC?.PathToVacuum?.IsOpened() ?? false)
				ZeroIfNeeded(MC?.Manometer, 5);

			if (VTT?.PathToVacuum?.IsOpened() ?? false)
				ZeroIfNeeded(VTT?.Manometer, 5);

			if (IM?.PathToVacuum?.IsOpened() ?? false)
				ZeroIfNeeded(IM?.Manometer, 10);

			if (GM?.PathToVacuum?.IsOpened() ?? false)
			{
				ZeroIfNeeded(GM?.Manometer, 10);
				foreach (var gr in GraphiteReactors)
					if (gr?.IsOpened ?? false)
						ZeroIfNeeded(gr.Manometer, 5);
			}
		}

		#endregion Periodic system activities & maintenance

		#region Process Management

		protected override void BuildProcessDictionary()
		{
			Separators.Clear();

			ProcessDictionary["Run samples"] = RunSamples;
			Separators.Add(ProcessDictionary.Count);

			ProcessDictionary["Prepare GRs for new iron and desiccant"] = PrepareGRsForService;
			ProcessDictionary["Replace iron in sulfur traps"] = ChangeSulfurFe;
			ProcessDictionary["Precondition GR iron"] = PreconditionGRs;
			ProcessDictionary["Evacuate d13C Port"] = Evacuate_d13CPort;
			Separators.Add(ProcessDictionary.Count);

			ProcessDictionary["Prepare carbonate sample for acid"] = PrepareCarbonateSample;
			ProcessDictionary["Load acidified carbonate sample"] = LoadCarbonateSample;
			Separators.Add(ProcessDictionary.Count);

			ProcessDictionary["Collect CO2 from InletPort"] = Collect;
			ProcessDictionary["Extract"] = Extract;
			ProcessDictionary["Measure"] = Measure;
			ProcessDictionary["Discard excess CO2 by splits"] = DiscardSplit;
			ProcessDictionary["Remove sulfur"] = RemoveSulfur;
			ProcessDictionary["Dilute small sample"] = Dilute;
			ProcessDictionary["Graphitize aliquots"] = GraphitizeAliquots;
			Separators.Add(ProcessDictionary.Count);

			ProcessDictionary["Collect etc."] = CollectEtc;
			ProcessDictionary["Extract etc."] = ExtractEtc;
			ProcessDictionary["Measure etc."] = MeasureEtc;
			ProcessDictionary["Graphitize etc."] = GraphitizeEtc;
			Separators.Add(ProcessDictionary.Count);

			ProcessDictionary["Open and evacuate line"] = OpenLine;
			ProcessDictionary["Admit dead CO2"] = AdmitDeadCO2;
			ProcessDictionary["Admit sealed CO2"] = AdmitSealedCO2IP;
			ProcessDictionary["Admit O2 into Inlet Port"] = AdmitIPO2;
			ProcessDictionary["Heat Quartz and Open Line"] = HeatQuartzOpenLine;
			ProcessDictionary["Turn Off CC Furnaces"] = TurnOffCCFurnaces;
			ProcessDictionary["Discard IP gases"] = DiscardIPGases;
			ProcessDictionary["Clean VTT"] = CleanVtt;
			ProcessDictionary["Freeze and Empty VTT"] = FreezeVtt;
			ProcessDictionary["Pressurize VTT..MC with inert gas"] = PressurizeVTT_MC;
			ProcessDictionary["Divide sample into aliquots"] = DivideAliquots;
			ProcessDictionary["Discard MC gases"] = DiscardMCGases;
			ProcessDictionary["Evacuate Inlet Port"] = EvacuateIP;
			ProcessDictionary["Flush Inlet Port"] = FlushIP;
			ProcessDictionary["add_d13C_He"] = AddHeTo_d13C;
			Separators.Add(ProcessDictionary.Count);

			ProcessDictionary["Exercise all Opened valves"] = ExerciseAllValves;
			ProcessDictionary["Exercise all LN Manifold valves"] = ExerciseLNValves;
			ProcessDictionary["Measure IP collection efficiency"] = MeasureIpCollectionEfficiency;
			ProcessDictionary["Transfer CO2 in MC to VTT"] = TransferCO2FromMCToVTT;
			ProcessDictionary["Tranfer CO2 from MC to GR"] = TransferCO2FromMCToGR;
			ProcessDictionary["Transfer CO2 from MC to IP"] = TransferCO2FromMCToIP;
			ProcessDictionary["Transfer CO2 from last GR to MC"] = TransferCO2FromGRToMC;
			ProcessDictionary["Calibrate all multi-turn valves"] = CalibrateRS232Valves;
			ProcessDictionary["Calibrate MC volume (KV in MCU)"] = CalibrateVolumeMC;
			ProcessDictionary["Calibrate all volumes from MC"] = CalibrateAllVolumesFromMC;
			ProcessDictionary["Check GR H2 density ratios"] = CalibrateGRH2;
			ProcessDictionary["Calibrate d13C He pressure"] = Calibrate_d13CHe;
			ProcessDictionary["Test"] = Test;

			base.BuildProcessDictionary();
		}

		protected virtual void Dilute()
		{
			//if (Sample.MicrogramsCarbon > SmallSampleMicrogramsCarbon) return;

			//double ugCdg_needed = (double)DilutedSampleMicrogramsCarbon - Sample.MicrogramsCarbon;

			//ProcessStep.Start("Dilute sample");

			//Notice.Send("Sample Alert!", $"Small sample! ({Sample.MicrogramsCarbon:0.0} ugC) Diluting...", Notice.Type.Tell);
			////Alert("Sample Alert!", $"Small sample! ({Sample.MicrogramsCarbon:0.0} ugC) Diluting...");

			//// Should we pre-evacuate the CuAg via VTT? The release of incondensables 
			//// later should remove any residuals now present in CuAg. And the only trash
			//// in the CuAg would have come from the sample anyway, and should not contain 
			//// condensables. It's probably better to avoid possible water from the VTT, 
			//// although that could be prevented also by re-freezing it, or keeping it frozen
			//// longer, at the expense of some time, in either case. 
			//// We could clean the VTT as soon as the sample has been extracted into the MC;
			//// then that PathToVacuum would be available, although the VTT might still have an 
			//// elevated pressure at this point.
			//// If the VTT were clean at this point, we could hold the sample there, and also
			//// transfer the dilution gas there, and extract the mixture for a cleaner sample.

			//#region Required
			//// These objects are required to run this method. Should we do a null check?
			//var CuAg = Find<Section>("CuAg");

			//var VTT_CuAg = Section.Connections(VTT, CuAg);
			//var CuAg_MC = Section.Connections(CuAg, MC);

			//var ftc_CuAg = CuAg.Coldfinger;
			//var ftc_MC = MC.Coldfinger;

			//var gs = GasSupply("CO2", MC);

			//#endregion Required

			//VTT_CuAg.Close();
			//ftc_CuAg.Freeze();

			//ftc_MC.Thaw();
			//CuAg_MC.Open();

			//ProcessSubStep.Start("Wait for MC coldfinger to thaw.");
			//while (ftc_MC.Temperature < MC.Temperature - 5) Wait();
			//ProcessSubStep.End();

			//ProcessSubStep.Start("Wait for sample to freeze in the CuAg coldfinger.");
			//while (ProcessSubStep.Elapsed.TotalMinutes < 1 ||
			//		(ugCinMC > 0.5 || ugCinMC.RateOfChange < 0) &&
			//		ProcessSubStep.Elapsed.TotalMinutes < 4)
			//	Wait();
			//Wait(30000);
			//ProcessSubStep.End();

			//CuAg.Coldfinger.Raise();

			//ProcessSubStep.Start("Wait 15 seconds with LN raised.");
			//Wait(15000);
			//CuAg_MC.Close();
			//ProcessSubStep.End();

			//// get the dilution gas into the MC
			//AdmitDeadCO2(ugCdg_needed);

			//// At this point, it would be useful to pass the dilution gas
			//// through the VTT. But how to make that possible?

			//// discard unused dilution gas, if necessary
			//gs?.Path?.JoinToVacuum();
			//Evacuate();

			//ProcessSubStep.Start("Take measurement");
			//WaitForMCStable();
			//Sample.MicrogramsDilutionCarbon = ugCinMC;
			//SampleLog.Record($"Dilution gas measurement:\t{Sample.MicrogramsDilutionCarbon:0.0}\tugC");
			//ProcessSubStep.End();

			//ProcessSubStep.Start("Freeze dilution gas");
			//ftc_MC.FreezeWait();

			//while (ProcessSubStep.Elapsed.TotalSeconds < 5 ||
			//	(ugCinMC > 0.5 || ugCinMC.RateOfChange < 0) &&
			//		ProcessSubStep.Elapsed.TotalMinutes < 1)
			//	Wait();
			//ProcessSubStep.End();

			//ProcessSubStep.Start("Combine sample and dilution gas");
			//ftc_CuAg.Thaw();
			//CuAg_MC.Open();

			//while (ProcessSubStep.Elapsed.TotalSeconds < 30 ||
			//		(ftc_CuAg.Temperature < 0 || ugCinMC > 0.5 || ugCinMC.RateOfChange < 0) &&
			//		ProcessSubStep.Elapsed.TotalMinutes < 2)
			//	Wait();

			//ftc_MC.RaiseLN();

			//CuAg_MC.Close();
			//ProcessSubStep.End();

			//ftc_CuAg.Standby();
			//ProcessSubStep.End();

			//ProcessStep.End();

			//// measure diluted sample
			//Measure();
		}

		protected virtual void CleanVtt() => VTT.Clean();

		// normally enters with MC..GM joined and evacuating via Split-VM
		// (except when run as an independent process from the UI, perhaps)
		protected virtual void AddHeTo_d13C()
		{
			if (!Sample.Take_d13C) return;

			#region Required

			var GM_d13C = Section.Combine(GM, d13C);

			var ftc_VP = VP.Coldfinger;

			var gs = InertGasSupply(GM_d13C);

			#endregion Required

			ProcessStep.Start("Add 1 atm He to vial");

			ftc_VP.WaitForLNpeak();

			ProcessSubStep.Start("Release incondensables");
			GM_d13C.Isolate();
			VP.Open();
			GM_d13C.OpenAndEvacuate(CleanPressure);
			VP.Close();
			ProcessSubStep.End();

			// desired final vial pressure, at normal room temperature
			var pTarget = PressureOverAtm;
			var nTarget = Particles(pTarget, VP.MilliLiters, RoomTemperature);
			var n_CO2 = Sample.Micrograms_d13C * CarbonAtomsPerMicrogram;

			// how much the GM pressure needs to fall to produce pVial == pTarget
			var dropTarget = Pressure(nTarget - n_CO2, GM.MilliLiters + d13C.MilliLiters, GM.Temperature);

			// TODO: replace VPInitialHePressure constant with a method that 
			// determines the initial GM gas pressure from dropTarget;

			var pa = AdmitGasToPort(
				gs,
				VPInitialHePressure,
				VP);
			var pHeInitial = pa[0];
			var pHeFinal = pa[1];

			var n_He = Particles(pHeInitial - pHeFinal, GM.MilliLiters + d13C.MilliLiters, GM.Temperature);
			var n = n_He + n_CO2;
			Sample.d13CPartsPerMillion = 1e6 * n_CO2 / n;

			// approximate standard-room-temperature vial pressure (neglects needle port volume)
			double pVP = Pressure(n, VP.MilliLiters, RoomTemperature);

			SampleLog.Record(
				$"d13C measurement:\r\n\t{Sample.LabId}\r\n" +
				$"\tGraphite {Sample.Aliquots[0].Name}" +
				$"\td13C:\t{Sample.Micrograms_d13C:0.0}\tugC" +
				$"\t{Sample.d13CPartsPerMillion:0}\tppm" +
				$"\tvial pressure:\t{pVP:0} / {pTarget:0}\tTorr"
			);

			double pVP_Error = pVP - pTarget;
			if (Math.Abs(pVP_Error) > VPErrorPressure)
			{
				SampleLog.Record("Sample Alert! Vial pressure out of range");
				SampleLog.Record(
					$"\tpHeGM: ({pHeInitial:0} => {pHeFinal:0}) / " +
					$"({VPInitialHePressure:0} => {VPInitialHePressure - dropTarget})");
				Notice.Send("Sample Alert!", $"Vial He pressure error: {pVP_Error:0}", Notice.Type.Tell);
				if (pVP_Error > 3 * VPErrorPressure || pVP_Error < -2 * VPErrorPressure)
				{
					Notice.Send("Error!",
						"Vial He pressure out of range." +
						"\r\nProcess paused.");
					// anything to do here, after presumed remedial action?
				}
			}

			ftc_VP.Thaw();
			VP.State = LinePort.States.Complete;

			ProcessStep.End();
			// exits with GM..d13C filled with He
		}

		protected override void OpenLine()
		{
			ProcessStep.Start("Open line");

			ProcessSubStep.Start("Close gas supplies");
			foreach (GasSupply g in GasSupplies.Values)
			{
				if (g.Destination.VacuumSystem == VacuumSystem)
					g.ShutOff();
			}

			// close gas flow valves after all shutoff valves are closed
			foreach (GasSupply g in GasSupplies.Values)
			{
				if (g.Destination.VacuumSystem == VacuumSystem)
					g.FlowValve?.CloseWait();
			}

			ProcessSubStep.End();

			foreach (var c in Chambers.Values)
				if (c.Dirty) c.Clean();

			var IM_VTT = Find<Section>("IM_VTT") ?? Section.Combine(IM, VTT);
			var VTT_CuAg = Find<Section>("VTT_CuAg");
			var CuAg_MC = Find<Section>("CuAg_MC");
			var CuAg_d13C = Find<Section>("CuAg_d13C");
			var GM_d13C = Find<Section>("GM_d13C");

			//bool vmOpened = VacuumSystem.State == HACS.Components.VacuumSystem.StateCode.HighVacuum;
			bool imWasOpened = IM.IsOpened;
			bool vttWasOpened = VTT.IsOpened;
			bool cuAg_d13CWasOpened = CuAg_d13C.IsOpened && MC.Ports.All(p => p.IsOpened) &&
				PreparedGRsAreOpened() &&
				(VP.IsOpened || ShouldBeClosed(VP));

			if (/*vmOpened && */imWasOpened && vttWasOpened && cuAg_d13CWasOpened &&
				IM_VTT.IsOpened &&
				VTT_CuAg.IsOpened)
			{
				Evacuate();
				return;
			}

			if (!cuAg_d13CWasOpened)
			{
				ProcessSubStep.Start($"Evacuate {CuAg_d13C.Name ?? "CuAg through GM"} and ready GRs");
				VacuumSystem.IsolateManifold();
				if (!ShouldBeClosed(VP))
					VP.Open();
				OpenPreparedGRs();
				CuAg_d13C.OpenAndEvacuate(OkPressure);
				ProcessSubStep.End();
			}

			//TODO are d13C and vp different?
			//bool doD13C = GM_d13C.InternalValves.IsClosed();


			// make sure the VM is empty
			//if (!vmOpened) Evacuate(OkPressure);

			if (!vttWasOpened)
			{
				ProcessSubStep.Start("Evacuate VTT");
				VacuumSystem.IsolateManifold();
				VTT.OpenAndEvacuate(OkPressure);
				ProcessSubStep.End();
			}

			if (!imWasOpened)
			{
				ProcessSubStep.Start("Evacuate IM");
				VacuumSystem.IsolateManifold();
				IM.OpenAndEvacuate(OkPressure);
				ProcessSubStep.End();
			}

			ProcessSubStep.Start("Join and evacuate all empty sections");
			VTT.JoinToVacuum();
			CuAg_d13C.JoinToVacuum();

			IM_VTT.Open();
			VTT_CuAg.Open();
			ProcessSubStep.End();
			ProcessStep.End();
		}

		#endregion Process Management

		#region Other calibrations

		protected virtual void CalibrateGRH2() =>
			CalibrateGRH2(GraphiteReactors.Where(gr => gr.Prepared).ToList());

		protected virtual void Calibrate_d13CHe()
		{
			#region Required

			var ftc_VP = VP.Coldfinger;

			var gs = InertGasSupply(Section.Combine(GM, d13C));

			#endregion Required

			if (ShouldBeClosed(VP))
			{
				Notice.Send("Calibration error", "calibrate_d13C_He: the VP is not available", Notice.Type.Tell);
				return;
			}

			SampleLog.WriteLine();
			SampleLog.Record("d13C He Calibration");
			SampleLog.Record($"pInitial\tpFinal\tpTarget\tpVP\terror");

			OpenLine();
			CloseAllGRs();

			ftc_VP.RaiseLN();

			bool adjusted = false;
			do
			{
				VacuumSystem.WaitForPressure(OkPressure);
				VP.Close();
				var pa = AdmitGasToPort(
					gs,
					VPInitialHePressure,
					VP);
				VP.Open();
				GM.Evacuate();

				var pTarget = PressureOverAtm;
				var nTarget = Particles(pTarget, VP.MilliLiters, RoomTemperature);
				var dropTarget = Pressure(nTarget, GM.MilliLiters + d13C.MilliLiters, GM.Temperature);
				var n = Particles(pa[0] - pa[1], GM.MilliLiters + d13C.MilliLiters, GM.Temperature);
				var p = Pressure(n, VP.MilliLiters, RoomTemperature);

				var error = pTarget - p;
				if (Math.Abs(error) / VPErrorPressure > 0.4)
				{
					var multiplier = pTarget / p;       // no divide-by-zero check
					VPInitialHePressure *= multiplier;
					adjusted = true;
				}
				SampleLog.Record($"{pa[0]:0}\t{pa[1]:0}\t{pTarget:0}\t{p:0}\t{error:0.0}");

			} while (adjusted);

			ftc_VP.Thaw();
			OpenLine();
		}
		#endregion Other calibrations

		#region Test functions

		protected override void Test()
		{
			for (int i = 0; i < 10; ++i)
			{
				if (i > 0) WaitMinutes(1);
				ExerciseAllValves();
			}
			//VolumeCalibration.Find("MC").Calibrate();
			//InletPort = SelectedInletPort;
			//var g = Find<GasSupply>(InletPort.SampleInfo?.LabId);
			//g?.Pressurize(Sample.Milligrams);
			//InletPort = null;

			//pressurizeVTT_MC();
		}

		#endregion Test functions
	}
}