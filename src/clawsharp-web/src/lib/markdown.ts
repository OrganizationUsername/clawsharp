/**
 * Pure TypeScript markdown-to-safe-HTML renderer.
 * Handles: fenced code blocks, headings, HR, ul/ol lists,
 * inline code, bold, italic, links.
 * HTML-escapes all raw input before processing.
 */

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

function inlineMarkdown(s: string): string {
  // Inline code — must come before bold/italic
  s = s.replace(/`([^`]+)`/g, '<code>$1</code>');
  // Bold
  s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  // Italic
  s = s.replace(/\*(.+?)\*/g, '<em>$1</em>');
  // Links
  s = s.replace(
    /\[([^\]]+)\]\(([^)]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener">$1</a>',
  );
  return s;
}

export function renderMarkdown(raw: string): string {
  if (!raw) return '';

  // Step 1: extract fenced code blocks before escaping so we don't
  // double-escape their content.
  type CodeBlock = { placeholder: string; html: string };
  const codeBlocks: CodeBlock[] = [];

  // Replace fenced code blocks with unique placeholders
  let processed = raw.replace(/```(\w*)\n?([\s\S]*?)```/g, (_, lang: string, code: string) => {
    const escapedCode = escapeHtml(code.trimEnd());
    const copyBtn =
      '<button class="copy-btn" onclick="(function(b){var c=b.closest(\'pre\').querySelector(\'code\').textContent;navigator.clipboard.writeText(c).then(function(){b.textContent=\'copied!\';setTimeout(function(){b.textContent=\'copy\';},1500);});})(this)">copy</button>';
    const html = `<pre><code class="lang-${lang}">${escapedCode}</code>${copyBtn}</pre>`;
    const placeholder = `\x00CODEBLOCK${codeBlocks.length}\x00`;
    codeBlocks.push({ placeholder, html });
    return placeholder;
  });

  // Step 2: escape remaining HTML in non-code regions
  // We split around placeholders, escape each region, then rejoin
  const parts = processed.split(/(\x00CODEBLOCK\d+\x00)/);
  processed = parts
    .map((part) => {
      if (/^\x00CODEBLOCK\d+\x00$/.test(part)) return part;
      return escapeHtml(part);
    })
    .join('');

  // Step 3: process block-level markdown line by line
  const lines = processed.split('\n');
  const out: string[] = [];
  let inList = false;
  let listType = '';

  const closeList = () => {
    if (inList) {
      out.push(listType === 'ul' ? '</ul>' : '</ol>');
      inList = false;
      listType = '';
    }
  };

  for (const line of lines) {
    // Restore code block placeholders — these lines must pass through as-is
    if (/^\x00CODEBLOCK\d+\x00$/.test(line.trim())) {
      closeList();
      const block = codeBlocks.find((b) => line.trim() === b.placeholder);
      out.push(block ? block.html : line);
      continue;
    }

    // Headings
    const h1 = line.match(/^# (.+)/);
    const h2 = line.match(/^## (.+)/);
    const h3 = line.match(/^### (.+)/);
    if (h3) {
      closeList();
      out.push(`<h3>${inlineMarkdown(h3[1])}</h3>`);
      continue;
    }
    if (h2) {
      closeList();
      out.push(`<h2>${inlineMarkdown(h2[1])}</h2>`);
      continue;
    }
    if (h1) {
      closeList();
      out.push(`<h1>${inlineMarkdown(h1[1])}</h1>`);
      continue;
    }

    // Horizontal rule
    if (/^---+$/.test(line.trim())) {
      closeList();
      out.push('<hr>');
      continue;
    }

    // Unordered list item
    const ulItem = line.match(/^[-*+] (.+)/);
    if (ulItem) {
      if (!inList || listType !== 'ul') {
        if (inList) out.push('</ol>');
        out.push('<ul>');
        inList = true;
        listType = 'ul';
      }
      out.push(`<li>${inlineMarkdown(ulItem[1])}</li>`);
      continue;
    }

    // Ordered list item
    const olItem = line.match(/^\d+\. (.+)/);
    if (olItem) {
      if (!inList || listType !== 'ol') {
        if (inList) out.push('</ul>');
        out.push('<ol>');
        inList = true;
        listType = 'ol';
      }
      out.push(`<li>${inlineMarkdown(olItem[1])}</li>`);
      continue;
    }

    // Non-list line: close any open list
    closeList();

    // Blank line
    if (line.trim() === '') {
      out.push('<p></p>');
      continue;
    }

    // Regular paragraph
    out.push(`<p>${inlineMarkdown(line)}</p>`);
  }

  closeList();
  return out.join('');
}
