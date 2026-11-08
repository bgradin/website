# Custom Frame API operations

The ASP.NET Core server implements public `GET /api/custom-frame/v1/releases`
and authenticated `GET /api/custom-frame/v1/config`. Terminate HTTPS at the
server's reverse proxy. Disable proxy/shared caching for config, including errors;
do not log `AuthKey`, provider credentials, or configuration response bodies.
Configure request throttling at the proxy with JSON errors and `Retry-After`.
Unknown paths under `/api/custom-frame` return JSON 404 errors.

Set these environment variables to absolute paths outside the web root:

```
CustomFrame__CatalogPath=/run/custom-frame/catalog.json
CustomFrame__SecretsPath=/run/secrets/custom-frame.json
```

Restrict secret-file permissions to the service account. Keep production files out
of Git. Both files are read on each request: replace complete files using atomic
rename on the same filesystem. No restart is required for key approval/revocation,
Dropbox credential rotation, or support-policy changes. Missing or invalid data returns
a generic 503 and `Retry-After: 60`; missing keys return 401 even during an outage.
Approved keys have no automatic expiration. Removing a key revokes it on the next
request. Use distinct, high-entropy keys; administration is outside this API.

An empty release catalog is valid for history, but config returns 503 until a
recommended published release and its support policy exist:

```json
{
  "releases": [],
  "recommendedVersionCode": 0,
  "supportedDevices": {}
}
```

Example catalog structure (replace metadata with values from a real signed APK):

```json
{
  "releases": [
    {
      "appVersion": "1.0",
      "appVersionCode": 1,
      "publishedAt": "2026-11-07T18:00:00Z",
      "releaseNotes": "Initial release.",
      "applicationId": "com.bgradin.customframe",
      "minSdk": 25,
      "download": {
        "url": "https://frames.example.com/downloads/custom-frame/1/custom-frame.apk",
        "sizeBytes": 12345678,
        "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
      }
    }
  ],
  "recommendedVersionCode": 1,
  "supportedDevices": { "1": [] }
}
```

Empty device arrays deliberately approve no devices for automatic updates. Replace
with rules containing actual tested `manufacturer`, `model`, and positive `minSdk`
values. Unknown rule properties are rejected to avoid changing matching semantics.
Policies can include older published releases. Missing older entries approve no
devices. The recommended release must have an entry. Display versions need not be
semantic versions. Both config version fields come from the selected catalog record.

Secret-file structure (illustrative placeholders only):

```json
{
  "approvedKeys": ["REPLACE_WITH_RANDOM_LONG_LIVED_KEY"],
  "sources": {
    "dropbox": {
      "name": "Dropbox",
      "appKey": "REPLACE_WITH_DROPBOX_APP_KEY",
      "appSecret": "REPLACE_WITH_DROPBOX_APP_SECRET",
      "refreshToken": "REPLACE_WITH_OFFLINE_REFRESH_TOKEN"
    }
  }
}
```

Use `"sources": {}` when no provider is offered. Only Dropbox is currently supported.
The server stores durable Dropbox credentials in this secret file. Access tokens are
cached only in memory and obtained automatically; do not put an `accessToken` in
this file. This replaces the previous manual access-token format. The public
configuration response is unchanged: each source contains only `name` and
`accessToken`. Refresh tokens and app credentials are never included.

## Dropbox authorization and refresh

Authorize the Dropbox app once using the OAuth authorization-code flow with
`token_access_type=offline` and the minimum read scopes required by the frame.
Exchange the authorization code server-side at `https://api.dropboxapi.com/oauth2/token`
and save the returned refresh token with the app key and secret above. Authorization
and credential administration remain operator tasks; no OAuth callback or token
administration endpoint is provided here. See [Dropbox's OAuth guide](https://docs.dropboxapi.com/dropbox-api/docs/oauth).

On an authenticated configuration request, the server exchanges the refresh token
for a short-lived access token using `grant_type=refresh_token`. It uses the returned
`expires_in` rather than assuming a fixed lifetime, and refreshes on demand up to
five minutes before expiry (10% of the lifetime for shorter-lived tokens). There is
no background refresh timer. Concurrent requests share one refresh per process;
multiple server instances maintain independent caches. Restarting clears the cache.

Requests to Dropbox have a ten-second timeout, disable redirects, and disable HTTP
client logging. Credentials are sent in the HTTPS form body, never in URLs. Upstream
errors and malformed token responses produce the generic uncached 503 response with
`Retry-After: 60`; refresh attempts back off for 60 seconds. The server does not
return the previous token after a refresh failure. Replacing any Dropbox credential
invalidates the cached token and backoff on the next authorized request. Removing
the source immediately removes it from configuration.

Refresh tokens can be revoked. If authorization is revoked, authorize again and
replace the credentials atomically. API auth keys remain independent of Dropbox
credentials. Frames should fetch config before each photo sync, use one snapshot
per job, and refetch once after Dropbox authentication failure before backing off.
Cached photos remain available during failures.

## Publishing

1. Build/sign a universal APK with the expected package and signing identity.
2. Calculate its byte length and lowercase SHA-256 (`sha256sum`), upload to a
   permanent HTTPS URL, then download and verify the uploaded bytes. Configure the
   artifact host to serve `application/vnd.android.package-archive`, immutable
   caching, and 404 for missing artifacts. Artifact hosting is separate from these
   two endpoints; the API validates metadata but does not fetch remote APKs.
3. Append a release with a unique version code higher than all previous codes.
   Never modify or remove published records/artifacts. Atomically replace the
   catalog while leaving the recommended version unchanged.
4. After hardware compatibility testing, add its device rules and advance
   `recommendedVersionCode` in a second atomic catalog replacement.

Publication ordering and immutability are operator responsibilities for this
read-only API. Do not use expiring artifact URLs. Existing debug-build signing
migration must be settled before distributing updates.

History accepts `limit` from 1 through 100 (default 20) and opaque `cursor` values
returned as `nextCursor`. It returns descending version codes and public five-minute
caching with ETag/If-None-Match support. Configuration always uses `no-store`.

Run the executable contract checks with:

```
dotnet run --project tests/CustomFrame/CustomFrame.csproj
```
