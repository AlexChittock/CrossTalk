﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Crosstalk.Common
{

    public interface IPartialResolver<T>
    {
        T GetById(string id);
    }
}
