<?php

declare(strict_types=1);

/**
 * The router for the built-in server that {@see \LabelZoom\Sdk\Tests\CurlHttpClientTest} starts.
 *
 * Echoes back what it was sent so the test can assert the request cURL actually put on the
 * wire, and can be asked for a specific status or an oversized body.
 */

$path = (string) parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH);

if ($path === '/status') {
    parse_str((string) parse_url((string) ($_SERVER['REQUEST_URI'] ?? ''), PHP_URL_QUERY), $query);
    http_response_code((int) ($query['code'] ?? 200));
    header('Content-Type: application/json');
    header('X-LZ-Request-Id: req-from-server');
    echo '{"message":"scripted"}';

    return true;
}

if ($path === '/binary') {
    header('Content-Type: image/png');
    // Bytes that are not valid UTF-8, so a client that mangles encodings fails here.
    echo "\x89PNG\r\n\x1a\n\x00\xFF\xFE\x01";

    return true;
}

$headers = [];
foreach ($_SERVER as $key => $value) {
    if (str_starts_with((string) $key, 'HTTP_')) {
        $headers[strtolower(str_replace('_', '-', substr((string) $key, 5)))] = $value;
    }
}
if (isset($_SERVER['CONTENT_TYPE'])) {
    $headers['content-type'] = $_SERVER['CONTENT_TYPE'];
}

header('Content-Type: application/json');
echo json_encode([
    'method' => $_SERVER['REQUEST_METHOD'] ?? '',
    'uri' => $_SERVER['REQUEST_URI'] ?? '',
    'headers' => $headers,
    'body' => file_get_contents('php://input'),
], JSON_THROW_ON_ERROR);

return true;
