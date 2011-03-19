using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MCA
{
    class DelayedCall
    {
        Timer timer;

        public DelayedCall(Action action, TimeSpan delay)
        {
            timer = new Timer(new TimerCallback((o) =>
                {
                    action();
                    timer.Dispose();
                }), null, delay, TimeSpan.FromMilliseconds(-1));
        }
    }
}
