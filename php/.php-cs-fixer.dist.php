<?php

declare(strict_types=1);

$finder = PhpCsFixer\Finder::create()
    ->in([__DIR__ . '/src', __DIR__ . '/tests']);

return (new PhpCsFixer\Config())
    // CI lints on the oldest supported PHP, but a contributor on a newer one should get the
    // same answer rather than a hard stop.
    ->setUnsupportedPhpVersionAllowed(true)
    ->setRiskyAllowed(true)
    ->setRules([
        '@PSR12' => true,
        '@PSR12:risky' => true,
        'declare_strict_types' => true,
        'ordered_imports' => ['sort_algorithm' => 'alpha'],
        'no_unused_imports' => true,
        'single_quote' => true,
        'trailing_comma_in_multiline' => ['elements' => ['arrays', 'arguments', 'parameters']],
        'phpdoc_align' => false,
        'phpdoc_separation' => false,
        'concat_space' => ['spacing' => 'one'],
    ])
    ->setFinder($finder);
