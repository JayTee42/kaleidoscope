using System;
using System.IO;
using System.Runtime.InteropServices;
using LLVMSharp;

public class Module: IDisposable
{
	// The module reference
	public LLVMModuleRef Mod { get; private set; }

	// The builder reference
	public LLVMBuilderRef Builder { get; private set; }

	// The target machine
	private LLVMTargetMachineRef _targetMachine;

	// The function pass manager reference (or null if optimizations are disabled)
	private LLVMPassManagerRef? _funcPassManager;

	// Avoid double-disposed.
	private bool _isDisposed = false;

	// The static constructor is executed once to init LLVM itself.
	static Module()
	{
		// Init the x86 target.
	    LLVM.InitializeX86TargetInfo();
	    LLVM.InitializeX86Target();
	    LLVM.InitializeX86TargetMC();
	    LLVM.InitializeX86AsmPrinter();

	    // Init the ARM target.
	    LLVM.InitializeARMTargetInfo();
	    LLVM.InitializeARMTarget();
	    LLVM.InitializeARMTargetMC();
	    LLVM.InitializeARMAsmPrinter();

	    // Init the MIPS target.
	    LLVM.InitializeMipsTargetInfo();
	    LLVM.InitializeMipsTarget();
	    LLVM.InitializeMipsTargetMC();
	    LLVM.InitializeMipsAsmPrinter();
	}

	// Create a new module with the given name.
	public Module(string name, string targetTriple, bool useOptimizations)
	{
		// Create the module and the builder.
		this.Mod = LLVM.ModuleCreateWithName(name);
		this.Builder = LLVM.CreateBuilder();

		// Get the default target triple if null has been passed.
		if (targetTriple == null)
		{
			targetTriple = Marshal.PtrToStringAnsi(LLVM.GetDefaultTargetTriple());
		}

		// Select the target and assign it to the module.
		if (LLVM.GetTargetFromTriple(targetTriple, out var target, out var error))
		{
			throw new ArgumentException($"Failed to obtain target from triple '{ targetTriple }': { error }");
		}

		LLVM.SetTarget(this.Mod, targetTriple);

		// Create a target machine.
		this._targetMachine = LLVM.CreateTargetMachine(target, targetTriple, "generic", "", LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault, LLVMRelocMode.LLVMRelocPIC, LLVMCodeModel.LLVMCodeModelDefault);

		// Create a data layout from the machine and assign it to the module.
		var dataLayout = LLVM.CreateTargetDataLayout(_targetMachine);
		LLVM.SetModuleDataLayout(this.Mod, dataLayout);

		// Create the function pass manager if optimizations are enabled.
		if (useOptimizations)
		{
			var funcPassManager = LLVM.CreateFunctionPassManagerForModule(this.Mod);

			// Add some nice optimization passes.
            LLVM.AddBasicAliasAnalysisPass(funcPassManager);
            LLVM.AddPromoteMemoryToRegisterPass(funcPassManager);
            LLVM.AddInstructionCombiningPass(funcPassManager);
            LLVM.AddReassociatePass(funcPassManager);
            LLVM.AddGVNPass(funcPassManager);
            LLVM.AddCFGSimplificationPass(funcPassManager);

            // Initialize the manager and its passes and store it.
            LLVM.InitializeFunctionPassManager(funcPassManager);
            this._funcPassManager = funcPassManager;
		}
		else
		{
			this._funcPassManager = null;
		}
	}

	// The `IDisposable` pattern (e. g. https://stackoverflow.com/questions/898828/use-of-finalize-dispose-method-in-c-sharp)
	public void Dispose()
	{
        Dispose(true);
        GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (this._isDisposed)
		{
			return;
		}

		if (disposing)
		{
			// Dispose the function pass manager if present.
			if (this._funcPassManager is LLVMPassManagerRef funcPassManager)
			{
    			LLVM.DisposePassManager(funcPassManager);
			}

			// Dispose the builder.
    		LLVM.DisposeBuilder(this.Builder);

    		// Dispose the module.
    		LLVM.DisposeModule(this.Mod);
		}

		this._isDisposed = true;
	}

	// Apply optimization (if enabled) to the given function.
	public void Optimize(LLVMValueRef func)
	{
		if (this._funcPassManager is LLVMPassManagerRef funcPassManager)
		{
			LLVM.RunFunctionPassManager(funcPassManager, func);
		}
	}

	// Print the module as IR to a file at the given path.
	public void PrintIRToFile(string filePath)
	{
		if (LLVM.PrintModuleToFile(this.Mod, filePath, out string error))
		{
			throw new InvalidOperationException($"Failed to print IR to file: { error }");
		}
	}

	// Print the module as assembly code to a file at the given path.
	public void PrintAssemblyToFile(string filePath)
	{
		if (LLVM.TargetMachineEmitToFile(this._targetMachine, this.Mod, Marshal.StringToHGlobalAnsi(filePath), LLVMCodeGenFileType.LLVMAssemblyFile, out var error))
		{
			throw new InvalidOperationException($"Failed to print assembly to file: { error }");
		}
	}

	// Print the module as object code to a file at the given path.
	public void PrintObjectCodeToFile(string filePath)
	{
		if (LLVM.TargetMachineEmitToFile(this._targetMachine, this.Mod, Marshal.StringToHGlobalAnsi(filePath), LLVMCodeGenFileType.LLVMObjectFile, out var error))
		{
			throw new InvalidOperationException($"Failed to print assembly to file: { error }");
		}
	}
}
