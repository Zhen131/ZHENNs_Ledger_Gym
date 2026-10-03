using System;
using System.Collections.Generic;

namespace Gym.Runtime.Configuration
{
    public class GymConfigException : Exception
    {
        public GymConfigException(IReadOnlyList<string> errors)
            : base("Gym configuration is invalid:\n- " + string.Join("\n- ", errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }
}
