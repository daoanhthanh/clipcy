#!/usr/bin/env node
// Quick discovery surface check for Phase 4 verification

async function check(baseUrl) {
  const results = { errors: [], warnings: [], info: [] };
  
  try {
    // Check robots.txt
    const robotsRes = await fetch(`${baseUrl}/robots.txt`);
    if (!robotsRes.ok) {
      results.errors.push('robots.txt not found');
    } else {
      const robotsText = await robotsRes.text();
      if (!robotsText.includes('Sitemap:')) results.warnings.push('robots.txt missing Sitemap directive');
      results.info.push('✓ robots.txt present');
    }
    
    // Check llms.txt
    const llmsRes = await fetch(`${baseUrl}/llms.txt`);
    if (!llmsRes.ok) {
      results.errors.push('llms.txt not found');
    } else {
      const llmsText = await llmsRes.text();
      if (llmsText.length < 200) results.warnings.push('llms.txt is very short');
      results.info.push(`✓ llms.txt present (${llmsText.length} bytes)`);
    }
    
    // Check sample page metadata
    const pageRes = await fetch(`${baseUrl}/en/`);
    if (pageRes.ok) {
      const html = await pageRes.text();
      const checks = [
        { pattern: /<meta property="og:title"/, label: 'og:title' },
        { pattern: /<meta property="og:description"/, label: 'og:description' },
        { pattern: /<meta property="og:url"/, label: 'og:url' },
        { pattern: /<meta property="og:image"/, label: 'og:image' },
        { pattern: /<meta name="twitter:card"/, label: 'twitter:card' },
        { pattern: /<link rel="canonical"/, label: 'canonical' }
      ];
      
      checks.forEach(({ pattern, label }) => {
        if (pattern.test(html)) {
          results.info.push(`✓ ${label} present`);
        } else {
          results.errors.push(`${label} missing`);
        }
      });
      
      // Check markdown twin
      const mdRes = await fetch(`${baseUrl}/en/index.md`);
      if (mdRes.ok) {
        const mdText = await mdRes.text();
        if (mdText.startsWith('---')) {
          results.info.push('✓ Markdown twin with frontmatter');
        } else {
          results.warnings.push('Markdown twin missing frontmatter');
        }
      } else {
        results.errors.push('Markdown twin not found');
      }
    }
    
  } catch (err) {
    results.errors.push(`Scan failed: ${err.message}`);
  }
  
  return results;
}

const baseUrl = process.argv[2] || 'http://localhost:4321';
console.log(`Checking discovery surfaces at ${baseUrl}...\n`);

check(baseUrl).then(results => {
  results.info.forEach(m => console.log(m));
  if (results.warnings.length) {
    console.log('\nWarnings:');
    results.warnings.forEach(m => console.log(`  ⚠ ${m}`));
  }
  if (results.errors.length) {
    console.log('\nErrors:');
    results.errors.forEach(m => console.log(`  ✗ ${m}`));
    process.exit(1);
  } else {
    console.log('\n✅ All discovery surfaces validated');
    process.exit(0);
  }
}).catch(err => {
  console.error('Fatal error:', err);
  process.exit(2);
});
