using System;

namespace NSLSolver
{
    public class NSLSolverException : Exception
    {
        public int StatusCode { get; }
        public NSLSolverException(string message, int statusCode = 0) : base(message) => StatusCode = statusCode;
    }

    public class BadRequestException          : NSLSolverException { public BadRequestException(string m)          : base(m, 400) { } }
    public class AuthenticationException      : NSLSolverException { public AuthenticationException(string m)      : base(m, 401) { } }
    public class InsufficientBalanceException : NSLSolverException { public InsufficientBalanceException(string m) : base(m, 402) { } }
    public class TypeNotAllowedException      : NSLSolverException { public TypeNotAllowedException(string m)      : base(m, 403) { } }
    public class RateLimitException           : NSLSolverException { public RateLimitException(string m)           : base(m, 429) { } }
    public class SolveException               : NSLSolverException { public SolveException(string m, int s)        : base(m, s)   { } }
}
