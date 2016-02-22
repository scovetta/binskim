﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Semantics;
using System;

namespace Microsoft.CodeAnalysis.IL
{
    internal sealed class RoslynOperationVisitor : OperationWalker
    {
        private readonly Action<IOperation> _action;

        public RoslynOperationVisitor(Action<IOperation> action)
        {
            _action = action;
        }

        public override void DefaultVisit(IOperation operation)
        {
            _action(operation);
        }

        public static void Visit(IOperation root, Action<IOperation> action)
        {
            new RoslynOperationVisitor(action).Visit(root);
        }
    }
}