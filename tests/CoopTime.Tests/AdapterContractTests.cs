using System.IO.Compression;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace CoopTime.Tests;

// Inspect integration wiring without loading the game's types or SMAPI entry point.
public sealed class AdapterContractTests
{
    [Fact]
    public void ProductionAdapterUsesTestedCoordinatorsAndActualSinglePlayerPredicate()
    {
        using ModuleDefinition module = ReadPlugin();
        TypeDefinition controller = module.Types.Single(t => t.Name == "TimeController");
        Assert.Contains(controller.Fields, f => f.FieldType.FullName == "CoopTime.Core.HostSession");
        Assert.Contains(controller.Fields, f => f.FieldType.FullName == "CoopTime.Core.ClientSession");
        Assert.DoesNotContain(controller.Fields, f => f.FieldType.FullName is "CoopTime.Core.Ledger" or "CoopTime.Core.PeerStateTracker");
        MethodDefinition pause = controller.Methods.Single(m => m.Name == "IsLocallyPaused");
        Instruction[] il = pause.Body.Instructions.ToArray();
        int native = Array.FindIndex(il, op => op.Operand is MethodReference { Name: "shouldTimePass" });
        Assert.True(native > 0);
        Assert.Equal(OpCodes.Ldc_I4_1, il[native - 1].OpCode);
        Assert.Contains(il, op => op.Operand is MethodReference { Name: "doMainGameUpdates" });
        Assert.Contains(il, op => op.Operand is MethodReference { Name: "get_IsActiveNoOverlay" });
        Assert.Contains(il, op => op.Operand is MethodReference
        {
            Name: "IsPaused", DeclaringType.FullName: "CoopTime.Core.SinglePlayerTiming"
        });
        MethodDefinition clock = controller.Methods.Single(m => m.Name == "AdjustElapsed");
        Assert.Contains(clock.Body.Instructions, op => op.Operand is MethodReference
        {
            Name: "AdjustElapsed", DeclaringType.FullName: "CoopTime.Core.HostSession"
        });
        MethodDefinition transpile = module.Types.Single(t => t.Name == "GameClockPatch")
            .Methods.Single(m => m.Name == "Transpile");
        Assert.Contains(transpile.Body.Instructions, op => op.Operand is MethodReference
        {
            Name: "Rewrite", DeclaringType.FullName: "CoopTime.Mod.GameClockRewriter"
        });
    }

    [Fact]
    public void SaveLifecycleUsesPerSaveStorageAndNeverGlobalOrModFolderFiles()
    {
        using ModuleDefinition module = ReadPlugin();
        TypeDefinition controller = module.Types.Single(t => t.Name == "TimeController");
        foreach ((string method, string operation) in new[] { ("LoadSave", "Load"), ("SaveLedger", "Save") })
        {
            MethodDefinition adapter = controller.Methods.Single(m => m.Name == method);
            Assert.Contains(adapter.Body.Instructions, op => op.Operand is MethodReference { Name: "get_IsMainPlayer" });
            Assert.Contains(adapter.Body.Instructions, op => op.Operand is MethodReference called
                && called.Name == operation && called.DeclaringType.FullName == "CoopTime.Core.LedgerPersistence");
        }

        MethodReference[] calls = controller.Methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Where(op => op.Operand is MethodReference)
            .Select(op => (MethodReference)op.Operand).ToArray();
        Assert.Contains(calls, called => called.Name == "ReadSaveData");
        Assert.Contains(calls, called => called.Name == "WriteSaveData");
        Assert.DoesNotContain(calls, called => called.Name is "WriteGlobalData" or "ReadGlobalData" or "WriteJsonFile");
        MethodDefinition save = controller.Methods.Single(m => m.Name == "SaveLedger");
        Assert.Contains(save.Body.Instructions, op => op.Operand is MethodReference { Name: "EndDay" });

        using ZipArchive zip = ZipFile.OpenRead(ArchivePath());
        using MemoryStream coreStream = new();
        using (Stream entry = zip.GetEntry("CoopTimeLedger/CoopTime.Core.dll")!.Open())
        {
            entry.CopyTo(coreStream);
        }

        coreStream.Position = 0;
        using ModuleDefinition core = ModuleDefinition.ReadModule(coreStream);
        MethodDefinition day = core.Types.Single(t => t.Name == "HostSession").Methods.Single(m => m.Name == "StartDay");
        Assert.Contains(day.Body.Instructions, op => op.Operand is MethodReference { Name: "BeginDay" });
        Assert.DoesNotContain(day.Body.Instructions, op => op.Operand is MethodReference called
            && called.Name == "Reset" && called.DeclaringType.FullName is "CoopTime.Core.Ledger" or "CoopTime.Core.ClockMath");
    }

    [Fact]
    public void LifecycleInputAndNetworkingEventsAreRegistered()
    {
        using ModuleDefinition module = ReadPlugin();
        MethodDefinition entry = module.Types.Single(t => t.Name == "ModEntry").Methods.Single(m => m.Name == "Entry");
        string[] calls = entry.Body.Instructions.Where(op => op.Operand is MethodReference)
            .Select(op => ((MethodReference)op.Operand).Name).ToArray();
        foreach (string name in new[] { "SaveLoaded", "DayStarted", "DayEnding", "Saving", "ReturnedToTitle", "UpdateTicked",
            "ModMessageReceived", "PeerConnected", "PeerDisconnected", "Rendered" })
        {
            Assert.Contains($"add_{name}", calls);
        }

        Assert.DoesNotContain("add_ButtonPressed", calls);
        Assert.DoesNotContain("add_MouseWheelScrolled", calls);
    }

    [Fact]
    public void SnapshotSenderProjectsTheLedgerAndPassesAnExplicitPlayerId()
    {
        using ModuleDefinition module = ReadPlugin();
        MethodDefinition send = module.Types.Single(t => t.Name == "TimeController")
            .Methods.Single(m => m.Name == "SendSnapshot");
        Instruction[] il = send.Body.Instructions.ToArray();
        Assert.Equal(2, il.Count(op => op.Operand is MethodReference
        {
            Name: "ForPlayer", DeclaringType.FullName: "CoopTime.Core.LedgerSnapshot"
        }));
        Instruction sendCall = Assert.Single(il, op => op.Operand is MethodReference { Name: "SendMessage" });
        Assert.Equal(OpCodes.Stelem_I8, sendCall.Previous.OpCode);
    }

    [Fact]
    public void OverlayNeverSetsGameMenusAndModNeverFreezesGlobalGameStateOrStartsAProcess()
    {
        using ModuleDefinition module = ReadPlugin();
        foreach (MethodDefinition method in AllTypes(module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            foreach (Instruction op in method.Body.Instructions)
            {
                if (op.OpCode == OpCodes.Stsfld && op.Operand is FieldReference field)
                {
                    Assert.DoesNotContain(field.Name, new[] { "activeClickableMenu", "paused", "isTimePaused", "freezeControls" });
                }

                if (op.Operand is MethodReference called)
                {
                    Assert.DoesNotContain(called.Name, new[] { "set_IsPaused", "set_IsTimePaused" });
                    Assert.False(called.DeclaringType.FullName == "System.Diagnostics.Process" && called.Name == "Start");
                }
            }
        }
    }

    [Fact]
    public void PackageIsPortableManagedIL()
    {
        using ModuleDefinition module = ReadPlugin();
        Assert.True(module.Attributes.HasFlag(ModuleAttributes.ILOnly));
        Assert.False(module.Attributes.HasFlag(ModuleAttributes.Required32Bit));
        Assert.Contains(module.AssemblyReferences, a => a.Name == "CoopTime.Core");
    }

    private static ModuleDefinition ReadPlugin()
    {
        using ZipArchive zip = ZipFile.OpenRead(ArchivePath());
        MemoryStream stream = new();
        using Stream entry = zip.GetEntry("CoopTimeLedger/CoopTime.dll")!.Open();
        entry.CopyTo(stream);
        stream.Position = 0;
        return ModuleDefinition.ReadModule(stream, new ReaderParameters { ReadingMode = ReadingMode.Immediate });
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {
        foreach (TypeDefinition root in roots)
        {
            yield return root;
            foreach (TypeDefinition nested in AllTypes(root.NestedTypes))
            {
                yield return nested;
            }
        }
    }

    private static string ArchivePath() => Environment.GetEnvironmentVariable("COOPTIME_ARTIFACT")!;
}
