﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Reflection.PortableExecutable;

using Microsoft.CodeAnalysis.BinaryParsers.PortableExecutable;
using Microsoft.CodeAnalysis.IL.Sdk;
using Microsoft.CodeAnalysis.Sarif.Driver.Sdk;
using Microsoft.CodeAnalysis.Sarif;

namespace Microsoft.CodeAnalysis.IL.Rules
{
    [Export(typeof(ISkimmer<BinaryAnalyzerContext>)), Export(typeof(IRuleDescriptor))]
    public class EnableSafeSEH : BinarySkimmerBase
    {
        /// <summary>
        /// BA2018
        /// </summary>
        public override string Id { get { return RuleIds.EnableSafeSEHId; } }

        /// <summary>
        /// X86 binaries should enable the SafeSEH mitigation in order to minimize
        /// exploitable memory corruption issues. SafeSEH makes it more difficult
        /// to vulnerabilities that permit overwriting SEH control blocks on the
        /// stack, by verifying that the location to which a thrown SEH exception
        /// would jump is indeed defined as an exception handler in the source
        /// program (and not shellcode). To resolve this issue, supply the
        /// /SafeSEH flag on the linker command line. Note that you will need to
        /// configure your build system to supply this flag for x86 builds only,
        /// as the /SafeSEH flag is invalid when linking for ARM and x64.
        /// </summary>

        public override string FullDescription
        {
            get { return RuleResources.BA2018_EnableSafeSEH_Description; }
        }

        protected override IEnumerable<string> FormatSpecifierIds
        {
            get
            {
                return new string[] {
                    nameof(RuleResources.BA2018_Pass),
                    nameof(RuleResources.BA2018_Pass_NoSEH),
                    nameof(RuleResources.BA2018_Fail)};
            }
        }

        public override AnalysisApplicability CanAnalyze(BinaryAnalyzerContext context, out string reasonForNotAnalyzing)
        {
            PE portableExecutable = context.PE;
            AnalysisApplicability result = AnalysisApplicability.NotApplicableToSpecifiedTarget;

            reasonForNotAnalyzing = MetadataConditions.ImageIsNot32BitBinary;
            if (portableExecutable.Machine != Machine.I386) { return result; }

            reasonForNotAnalyzing = MetadataConditions.ImageIsXBoxBinary;
            if (portableExecutable.IsXBox) { return result; }

            reasonForNotAnalyzing = MetadataConditions.ImageIsResourceOnlyBinary;
            if (portableExecutable.IsResourceOnly) { return result; }

            reasonForNotAnalyzing = null;
            return AnalysisApplicability.ApplicableToSpecifiedTarget;
        }

        public override void Analyze(BinaryAnalyzerContext context)
        {
            PEHeader peHeader = context.PE.PEHeaders.PEHeader;

            /* IMAGE_DLLCHARACTERISTICS_NO_SEH */
            if ((peHeader.DllCharacteristics & DllCharacteristics.NoSeh) == DllCharacteristics.NoSeh)
            {
                // '{0}' is an x86 binary that does not use SEH, making it an invalid
                // target for exploits that attempt to replace SEH jump targets with 
                // attacker-controlled shellcode.	
                context.Logger.Log(this,
                    RuleUtilities.BuildResult(ResultKind.Pass, context, null,
                        nameof(RuleResources.BA2018_Pass_NoSEH)));
                return;
            }

            // This will not raise false positives for non-C and C++ code, because the above 
            // check for IMAGE_DLLCHARACTERISTICS_NO_SEH excludes things that don't actually 
            // handle SEH exceptions like .NET ngen'd code.
            if (peHeader.LoadConfigTableDirectory.RelativeVirtualAddress == 0)
            {
                // '{0}' is an x86 binary which does not contain a load configuration table, 
                // indicating that it does not enable the SafeSEH mitigation. SafeSEH makes 
                // it more difficult to exploit memory corruption vulnerabilities that can 
                // overwrite SEH control blocks on the stack, by verifying that the location 
                // to which a thrown SEH exception would jump is indeed defined as an 
                // exception handler in the source program (and not shellcode). To resolve 
                // this issue, supply the /SafeSEH flag on the linker command line. Note 
                // that you will need to configure your build system to supply this flag for 
                // x86 builds only, as the /SafeSEH flag is invalid when linking for ARM and x64.
                context.Logger.Log(this,
                    RuleUtilities.BuildResult(ResultKind.Error, context, null,
                        nameof(RuleResources.BA2018_Fail),
                        RuleResources.BA2018_Fail_NoLoadConfigurationTable));
                return;
            }

            SafePointer sp = new SafePointer(context.PE.ImageBytes, peHeader.LoadConfigTableDirectory.RelativeVirtualAddress);
            SafePointer loadConfigVA = context.PE.RVA2VA(sp);
            ImageLoadConfigDirectory32 loadConfig = new ImageLoadConfigDirectory32(peHeader, loadConfigVA);

            Int32 seHandlerSize = (Int32)loadConfig.GetField(ImageLoadConfigDirectory32.Fields.Size);
            if (seHandlerSize < 72)
            {
                // contains an unexpectedly small load configuration table {size 0}
                string seHandlerSizeText = String.Format(RuleResources.BA2018_Fail_LoadConfigurationIsTooSmall, seHandlerSize.ToString());

                context.Logger.Log(this,
                    RuleUtilities.BuildResult(ResultKind.Error, context, null,
                        nameof(RuleResources.BA2018_Fail),
                        RuleResources.BA2018_Fail_LoadConfigurationIsTooSmall,
                        seHandlerSizeText));
                return;
            }

            UInt32 seHandlerTable = (UInt32)loadConfig.GetField(ImageLoadConfigDirectory32.Fields.SEHandlerTable);
            UInt32 seHandlerCount = (UInt32)loadConfig.GetField(ImageLoadConfigDirectory32.Fields.SEHandlerCount);

            if (seHandlerTable == 0 || seHandlerCount == 0)
            {
                string failureKind = null;
                if (seHandlerTable == 0)
                {
                    // has an empty SE handler table in the load configuration table
                    failureKind = RuleResources.BA2018_Fail_EmptySEHandlerTable;
                }
                else if (seHandlerCount == 0)
                {
                    // has zero SE handlers in the load configuration table
                    failureKind = RuleResources.BA2018_Fail_NoSEHandlers;
                }

                // '{0}' is an x86 binary which {1}, indicating that it does not enable the SafeSEH 
                // mitigation. SafeSEH makes it more difficult to exploit memory corruption 
                // vulnerabilities that can overwrite SEH control blocks on the stack, by verifying 
                // that the location to which a thrown SEH exception would jump is indeed defined 
                // as an exception handler in the source program (and not shellcode). To resolve 
                // this issue, supply the /SafeSEH flag on the linker command line. Note that you 
                // will need to configure your build system to supply this flag for x86 builds only, 
                // as the /SafeSEH flag is invalid when linking for ARM and x64.
                context.Logger.Log(this,
                    RuleUtilities.BuildResult(ResultKind.Error, context, null,
                        nameof(RuleResources.BA2018_Fail), 
                        failureKind));
                return;
            }

            // ''{0}' is an x86 binary that enables SafeSEH, a mitigation that verifies SEH exception 
            // jump targets are defined as exception handlers in the program (and not shellcode).
            context.Logger.Log(this,
                RuleUtilities.BuildResult(ResultKind.Pass, context, null,
                    nameof(RuleResources.BA2018_Pass)));
        }
    }
}
