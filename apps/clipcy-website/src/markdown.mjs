/**
 * Generate Markdown twins from catalog content
 */

/**
 * Convert homepage catalog to clean Markdown
 */
export function renderHomeMarkdown(locale, catalog) {
  return `# ${catalog.meta.title}

${catalog.meta.description}

## ${catalog.problem.title}

${catalog.problem.body}

## ${catalog.features.heading}

### ${catalog.features.find.title}

${catalog.features.find.body}

*${catalog.features.find.label}*

### ${catalog.features.pin.title}

${catalog.features.pin.body}

*${catalog.features.pin.label}*

### ${catalog.features.preview.title}

${catalog.features.preview.body}

*${catalog.features.preview.label}*

## ${catalog.workflow.heading}

### ${catalog.workflow.step1.title}

${catalog.workflow.step1.body}

### ${catalog.workflow.step2.title}

${catalog.workflow.step2.body}

### ${catalog.workflow.step3.title}

${catalog.workflow.step3.body}

**Keyboard shortcuts:**

- ${catalog.workflow.shortcuts.open}
- ${catalog.workflow.shortcuts.navigate}
- ${catalog.workflow.shortcuts.paste}
- ${catalog.workflow.shortcuts.close}
- ${catalog.workflow.shortcuts.pin}

## ${catalog.trust.heading}

${catalog.trust.storage}

${catalog.trust.warning}

**${catalog.trust.support.windows}**

${catalog.trust.support.languages}

[${catalog.trust.privacyLink}](/${locale}/privacy/)

## ${catalog.faq.heading}

### ${catalog.faq.q1.question}

${catalog.faq.q1.answer}

### ${catalog.faq.q2.question}

${catalog.faq.q2.answer}

### ${catalog.faq.q3.question}

${catalog.faq.q3.answer}

### ${catalog.faq.q4.question}

${catalog.faq.q4.answer}

## ${catalog.finalCta.heading}

${catalog.finalCta.planned}

[${catalog.hero.ctaSecondary}](/${locale}/download/)
`;
}

/**
 * Convert download page catalog to clean Markdown
 */
export function renderDownloadMarkdown(locale, catalog, content) {
  const buildSteps = `
1. ${catalog.download.buildInstructions.steps.step1}

   \`\`\`bash
   git clone https://github.com/${content.repositoryOwner}.git
   cd clipcy
   \`\`\`

2. ${catalog.download.buildInstructions.steps.step2}

   \`\`\`bash
   cargo build --release -p clipcy-bridge
   \`\`\`

3. ${catalog.download.buildInstructions.steps.step3}

   \`\`\`bash
   uniffi-bindgen-cs --library target/release/clipcy_core.dll --out-dir apps/clipcy-windows-ui/Bindings
   \`\`\`

4. ${catalog.download.buildInstructions.steps.step4}

   \`\`\`powershell
   .\\scripts\\build-windows-ui.ps1 -Release
   \`\`\`

${catalog.download.buildInstructions.steps.runNote}
`;

  const noReleaseSection = !content.hasPublicRelease ? `
## ${catalog.download.noRelease.title}

${catalog.download.noRelease.body}
` : '';

  return `# ${catalog.download.heading}
${noReleaseSection}
## ${catalog.download.requirements.heading}

- ${catalog.download.requirements.runtime}
  - ${catalog.download.requirements.runtimeNote}

## ${catalog.download.buildInstructions.heading}

### ${catalog.download.buildInstructions.prerequisites.heading}

${catalog.download.buildInstructions.prerequisites.items.map(item => `- ${item}`).join('\n')}

### ${catalog.download.buildInstructions.steps.heading}
${buildSteps}
## ${catalog.download.usage.heading}

1. ${catalog.download.usage.step1}
2. ${catalog.download.usage.step2}
3. ${catalog.download.usage.step3}
4. ${catalog.download.usage.step4}
5. ${catalog.download.usage.step5}
`;
}

/**
 * Convert privacy page catalog to clean Markdown
 */
export function renderPrivacyMarkdown(locale, catalog) {
  return `# ${catalog.privacy.heading}

*${catalog.privacy.effectiveDate}: 2026-10-05*

## ${catalog.privacy.appData.heading}

${catalog.privacy.appData.clipboard}

${catalog.privacy.appData.storage}

${catalog.privacy.appData.previews}

**${catalog.privacy.appData.sensitive}**

${catalog.privacy.appData.settings}

## ${catalog.privacy.websiteData.heading}

- ${catalog.privacy.websiteData.noAnalytics}
- ${catalog.privacy.websiteData.noFonts}
- ${catalog.privacy.websiteData.noForms}
- ${catalog.privacy.websiteData.language}
- ${catalog.privacy.websiteData.hosting}

## ${catalog.privacy.changes.heading}

${catalog.privacy.changes.body}
`;
}
