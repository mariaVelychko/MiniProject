namespace Shared.Common
{
    public enum ServiceErrorType
    {
        Validation,
        NotFound,
        Conflict,
        Unauthorized,
        Forbidden,
    }

    public class ServiceResult<T>
    {
        public bool IsSuccess { get; }
        public T? Value { get; }
        public ServiceErrorType? ErrorType { get; }
        public string? Error { get; }

        private ServiceResult(bool isSuccess, T? value, ServiceErrorType? errorType, string? error)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorType = errorType;
            Error = error;
        }

        public static ServiceResult<T> Success(T value) => new(true, value, null, null);

        public static ServiceResult<T> Failure(ServiceErrorType type, string error) =>
            new(false, default, type, error);
    }

    public class ServiceResult
    {
        public bool IsSuccess { get; }
        public ServiceErrorType? ErrorType { get; }
        public string? Error { get; }

        private ServiceResult(bool isSuccess, ServiceErrorType? errorType, string? error)
        {
            IsSuccess = isSuccess;
            ErrorType = errorType;
            Error = error;
        }

        public static ServiceResult Success() => new(true, null, null);

        public static ServiceResult Failure(ServiceErrorType type, string error) =>
            new(false, type, error);
    }
}
