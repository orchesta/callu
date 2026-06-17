using System;

namespace Callu.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SkipApiResponseWrapperAttribute : Attribute
{
}
