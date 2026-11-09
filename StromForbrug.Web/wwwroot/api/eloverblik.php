<?php
// Relays StromForbrug's calls to Eloverblik's customer API.
//
// Why: a browser may only read another site's answer when it carries an
// Access-Control-Allow-Origin header, and Eloverblik's successful answers don't. The browser then
// throws the answer away and the app sees only "Failed to fetch". The app instead calls this
// script on its own site, which a browser always may read; the script, being a server, is not
// bound by that rule.
//
// Only the endpoints the app uses are passed on, so this is not an open proxy. The token is sent
// on to Eloverblik and nothing is stored or logged. Needs PHP 8 with the curl extension.
//
// Called as api/eloverblik.php/<Eloverblik path>, e.g. api/eloverblik.php/token.
declare(strict_types=1);

const UPSTREAM = 'https://api.eloverblik.dk/customerapi/api/';
const ROUTES = [
    '#^token$#' => 'GET',
    '#^meteringpoints/meteringpoints$#' => 'GET',
    '#^meterdata/gettimeseries/\d{4}-\d{2}-\d{2}/\d{4}-\d{2}-\d{2}/(Actual|Quarter|Hour|Day|Month|Year)$#' => 'POST',
];

function fail(int $status, string $message): void
{
    http_response_code($status);
    header('Content-Type: application/json');
    header('Cache-Control: no-store');
    echo json_encode(['error' => $message]);
    exit;
}

// Local development only: the app's dev server (localhost:5286) and this script (php -S on
// another port) are different origins, so allow localhost pages to read the answers. Production
// serves the app and this script from the same site and needs nothing here.
$origin = $_SERVER['HTTP_ORIGIN'] ?? '';
if (preg_match('#^https?://(localhost|127\.0\.0\.1)(:\d+)?$#', $origin)) {
    header('Access-Control-Allow-Origin: ' . $origin);
    header('Vary: Origin');
    if (($_SERVER['REQUEST_METHOD'] ?? '') === 'OPTIONS') {
        header('Access-Control-Allow-Methods: GET, POST');
        header('Access-Control-Allow-Headers: Authorization, Content-Type, Accept');
        header('Access-Control-Max-Age: 600');
        http_response_code(204);
        exit;
    }
}

if (!function_exists('curl_init')) fail(500, 'PHP\'s curl extension is not enabled.');

// The Eloverblik path comes after the script name.
$path = ltrim($_SERVER['PATH_INFO'] ?? '', '/');
$method = $_SERVER['REQUEST_METHOD'] ?? 'GET';

$route = null;
foreach (ROUTES as $pattern => $allowed) {
    if (preg_match($pattern, $path)) { $route = $allowed; break; }
}
if ($route === null) fail(404, 'Unknown Eloverblik path.');
if ($method !== $route) fail(405, "Use $route for this path.");

// Apache and PHP-FPM do not always pass Authorization on to PHP; look in every usual place.
$auth = $_SERVER['HTTP_AUTHORIZATION'] ?? $_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '';
if ($auth === '' && function_exists('getallheaders')) {
    $auth = array_change_key_case(getallheaders())['authorization'] ?? '';
}
if (strlen($auth) > 8000 || !preg_match('#^Bearer [A-Za-z0-9._~+/=-]+$#', $auth)) {
    fail(401, 'Missing or malformed bearer token.');
}

$query = ($_SERVER['QUERY_STRING'] ?? '') !== '' ? '?' . $_SERVER['QUERY_STRING'] : '';
$headers = ['Authorization: ' . $auth, 'Accept: application/json', 'User-Agent: StromForbrug/1.0'];

$options = [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_CUSTOMREQUEST => $method,
    CURLOPT_CONNECTTIMEOUT => 15,
    CURLOPT_TIMEOUT => 90,          // Eloverblik can be slow for a month of hourly data
];
if (PHP_OS_FAMILY === 'Windows') {
    // PHP on Windows ships without a CA bundle; use the certificates Windows trusts.
    $options[CURLOPT_SSL_OPTIONS] = CURLSSLOPT_NATIVE_CA;
}
if ($method === 'POST') {
    $body = file_get_contents('php://input', false, null, 0, 10000);
    $options[CURLOPT_POSTFIELDS] = $body === false ? '' : $body;
    $headers[] = 'Content-Type: application/json';
}
$options[CURLOPT_HTTPHEADER] = $headers;

$ch = curl_init(UPSTREAM . $path . $query);
curl_setopt_array($ch, $options);
$response = curl_exec($ch);
if ($response === false) fail(502, 'Eloverblik did not answer: ' . curl_error($ch));

$status = curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
$type = curl_getinfo($ch, CURLINFO_CONTENT_TYPE);
http_response_code($status);
header('Content-Type: ' . ($type ?: 'application/json'));
header('Cache-Control: no-store');
// Marks relayed answers in the browser's network log.
header('X-StromForbrug-Relay: 1');
echo $response;
