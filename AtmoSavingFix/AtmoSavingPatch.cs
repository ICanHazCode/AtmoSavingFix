using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Serialization;
using HarmonyLib;
using ICanHazCode.ModUtils;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using UnityEngine;
using SRE = System.Reflection.Emit;

namespace AtmoSavingFix
{
	[HarmonyPatch(typeof(XmlSaveLoad),nameof(XmlSaveLoad.GetWorldData))]
	class AtmoSavingPatch
	{

		#region Logging
		static void Log(string line)
		{
			Patch.Instance.Log(line);
		}

		static void LogError(string line)
		{
			Patch.Instance.LogError(line);
		}

		private static void printCode(IEnumerable<CodeInstruction> codes, Collection<VariableDefinition> locals, string header)
		{
			StringBuilder sb = new StringBuilder(header);
			sb.Append("\nCode:\n");
			//cecilGen.DefineLabel();

			sb.AppendLine(".locals(");
			foreach (VariableDefinition local in locals)
			{
				sb.AppendLine(string.Format("\t{0,-3}:\t{1}", local.Index, local.VariableType));
			}
			sb.AppendLine(")\n======================");
			int i = 0;

			foreach (CodeInstruction code in codes)
			{
				string ln = code.labels.Count > 0 ? string.Format("lbl[{0}]", code.labels[0].GetHashCode())
												: i.ToString();
				i++;
				sb.AppendLine(string.Format("{0,-8}:{1,-10}\t{2}",
											ln,
											code.opcode,
											code.operand is SRE.Label ? $"lbl[{code.operand.GetHashCode()}]" :
											code.operand is SRE.LocalBuilder lb ? $"Local:{lb.LocalType} ({lb.LocalIndex})" :
											code.operand is string ? $"\"{code.operand}\"" :
											code.operand is MethodBase info ? info.FullDescription() :
											code.operand
											)
							);
			}

			Log(sb.ToString());

		}
		#endregion

		#region Transpilers

		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> LoadGameDataTranspiler(IEnumerable<CodeInstruction> instructions,
																	SRE.ILGenerator generator,
																	MethodBase methodInfo)
		{
			var methodTry = methodInfo.GetMethodBody().ExceptionHandlingClauses;
			var locals = generator.GetGenVariables();
			var tryBlocks = generator.GetCecilGen().IL.Body.ExceptionHandlers;
			if (Patch.Debug) printCode(instructions, locals, "Before Transpiler:");
			bool fail = false;
			IEnumerable<CodeInstruction> codes;
			try
			{
				codes = XPiler(instructions, generator);
			}
			catch (Exception ex)
			{
				fail = true;
				LogError($"Error in Xpiler:\n{ex}");
				codes = null;
			}
			if (!fail && Patch.Debug) printCode(codes, locals, "After Transpiler");
			return fail ? instructions : codes;
		}

		static IEnumerable<CodeInstruction> XPiler(IEnumerable<CodeInstruction> instructions, SRE.ILGenerator generator)
		{
			var codes = instructions.ToList();
			//var gen = generator.GetCecilGen();
			SRE.LocalBuilder localAtmo = null;
			var atmoGetItem = AccessTools.PropertyGetter(typeof(List<Atmosphere>), "Item");
			if (Patch.Debug) Log($"List<Atmosphere>[] getter:{atmoGetItem.FullDescription()}");
			//Find local atmosphere LocalBuilder
			for (int index = 160;index < codes.Count;index++)
			{
				if(codes[index].opcode == SRE.OpCodes.Callvirt
					&& atmoGetItem.Equals(codes[index].operand))
				{
					localAtmo = codes[++index].operand as SRE.LocalBuilder;
					break;
				}
			}
			var fieldPlayerEnteredTiles = AccessTools.Field(typeof(Assets.Scripts.TileSystem), nameof(Assets.Scripts.TileSystem.PlayerEnteredTiles));
			//Insert extra check in the if statement
			for (int index = 165; index < codes.Count; index++)
			{
				//														/-------   Add This   -------\
				//if(!TileSystem.PlayerEnteredTiles.Contains(Vector2Int) && !atmosphere.IsValidThing())
				//find LoadField TileSystem.PlayerEnteredTiles
				if(codes[index].opcode == SRE.OpCodes.Ldsfld
					&& fieldPlayerEnteredTiles.Equals(codes[index].operand))
				{
					//Sanity Check
					//check if callvirt List<vector2Int>.Contains()
					if(codes[index + 2].opcode == SRE.OpCodes.Callvirt
						&& typeof(List<UnityEngine.Vector2Int>).GetMethod("Contains").Equals(codes[index+2].operand))
					{
						//we start 3 codes later
						index += 3;
						var truLabel = generator.DefineLabel();
						var jmpLabel = generator.DefineLabel();
						// jmp to jmpLabel if true
						codes.Insert(index++, new CodeInstruction(SRE.OpCodes.Brtrue_S, truLabel));
						// load atmosphere local
						codes.Insert(index++, new CodeInstruction(SRE.OpCodes.Ldloc_S, localAtmo));
						// callvirt bool Atmosphere.IsValidThing()
						codes.Insert(index++, new CodeInstruction(SRE.OpCodes.Callvirt,
																AccessTools.Method(
																		typeof(Atmosphere),
																		nameof(Atmosphere.IsValidThing))));
						index += 2;
						// add our label to point to this instruction
						codes.Insert(index++, new CodeInstruction(SRE.OpCodes.Br_S, jmpLabel));
						codes.Insert(index, new CodeInstruction(SRE.OpCodes.Ldc_I4_0));
						codes[index++].labels.Add(truLabel);
						codes[index].labels.Add(jmpLabel);
						break;
					}
				}
			}
			return codes;
		}
		#endregion
	}
}
