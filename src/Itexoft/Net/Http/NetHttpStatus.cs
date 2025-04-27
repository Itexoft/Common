// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Net.Http;

public enum NetHttpStatusClass
{
    Unknown = 0,
    Informational = 1,
    Success = 2,
    Redirection = 3,
    ClientError = 4,
    ServerError = 5,
}

public enum NetHttpStatus
{
    Unknown = 0,
    Continue = 100,
    SwitchingProtocols = 101,
    Ok = 200,
    Created = 201,
    Accepted = 202,
    NoContent = 204,
    PartialContent = 206,
    MovedPermanently = 301,
    Found = 302,
    SeeOther = 303,
    NotModified = 304,
    TemporaryRedirect = 307,
    PermanentRedirect = 308,
    BadRequest = 400,
    Unauthorized = 401,
    Forbidden = 403,
    NotFound = 404,
    RequestTimeout = 408,
    TooManyRequests = 429,
    InternalServerError = 500,
    BadGateway = 502,
    ServiceUnavailable = 503,
    GatewayTimeout = 504,
}

public enum NetHttpVersion
{
    Version10 = 10,
    Version11 = 11,
}

public static class NetHttpStatusExtensions
{
    public static NetHttpStatusClass GetClass(this NetHttpStatus status)
    {
        var code = (int)status;

        if (code <= 0)
            return NetHttpStatusClass.Unknown;

        return (NetHttpStatusClass)(code / 100);
    }
}
