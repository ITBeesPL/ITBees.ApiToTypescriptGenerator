using System;
using System.Collections.Generic;
using System.Reflection;

namespace ITBees.ApiToTypescriptGenerator.Services
{
    public class ActionInfo
    {
        public string ActionName { get; set; }
        public string HttpMethod { get; set; }
        public Type ReturnType { get; set; }
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
    }
}