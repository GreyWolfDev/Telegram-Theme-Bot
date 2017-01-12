using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThemeBot
{
    [Flags]
    public enum Access
    {
        Standard = 0,
        AutoApprove = 1,
        Moderator = 2,
        Admin = 4,
    }
}
