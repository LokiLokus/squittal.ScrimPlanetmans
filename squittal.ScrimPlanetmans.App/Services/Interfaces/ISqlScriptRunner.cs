﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace squittal.ScrimPlanetmans.Services
{
    public interface ISqlScriptRunner
    {
        void RunSqlScript(string fileName);
    }
}