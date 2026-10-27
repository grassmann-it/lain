#!/usr/bin/env python3
"""Build an entirely self-contained, offline provider referral research page."""
import base64
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
escape = lambda value: html.escape(str(value), quote=True)
rows = sum((json.loads((ROOT / f'docs/referral-research-{part}.json').read_text()) for part in ('a', 'b')), [])
assert len(rows) == 20 and len({row['id'] for row in rows}) == 20

def state(row):
    if row.get('programState') == 'ended':
        return 'ended', 'Ended'
    if row['status'] in ('public_referral', 'public_affiliate'):
        return 'confirmed', 'Referral confirmed' if row['status'] == 'public_referral' else 'Affiliate confirmed'
    return 'unknown', 'Unconfirmed'

def paragraphs(value):
    return ''.join('<p>' + escape(item) + '</p>' for item in (value if isinstance(value, list) else [value]) if item)

rows.sort(key=lambda row: ({'confirmed': 0, 'unknown': 1, 'ended': 2}[state(row)[0]], row['provider'].casefold()))
cards = []
for row in rows:
    category, label = state(row)
    benefit = row['benefit'] or ('This program has ended. Do not promote an active referral link here.' if category == 'ended'
        else 'Program documented; current reward terms remain unconfirmed.' if category == 'confirmed'
        else 'No public referral or affiliate program is documented for this product.')
    source_links = []
    for source in row['officialSources']:
        assert source['url'].startswith('https://')
        source_links.append('<li><a href="' + escape(source['url']) + '" target="_blank" rel="noopener noreferrer">' + escape(source['label']) + ' ↗</a></li>')
    search = json.dumps(row, ensure_ascii=False).lower()
    cards.append(f'''<article class="provider" data-id="{escape(row['id'])}" data-state="{category}" data-search="{escape(search)}">
      <div class="card-head"><span class="badge {category}">{label}</span><span class="provider-id">{escape(row['id'])}</span></div>
      <h2>{escape(row['provider'])}</h2><p class="benefit">{escape(benefit)}</p>
      <details><summary>Terms &amp; sources <span>{len(source_links)} sources</span></summary>
        <h3>Who is eligible?</h3>{paragraphs(row['eligibility'])}
        <h3>Product scope &amp; limitations</h3>{paragraphs(row['limitations'])}
        <h3>Official sources</h3><ul class="sources">{''.join(source_links)}</ul>
        <h3>Assessment</h3>{paragraphs(row['notes'])}<p class="checked">Checked on October 24, 2026</p>
      </details>
      <div class="own-link"><label for="link-{escape(row['id'])}">Your referral link</label><div class="link-row">
        <input id="link-{escape(row['id'])}" type="url" autocomplete="off" spellcheck="false" placeholder="https://…" aria-describedby="saved-{escape(row['id'])}">
        <a class="button own-open" hidden target="_blank" rel="noopener noreferrer">Open ↗</a></div>
        <small id="saved-{escape(row['id'])}" class="saved">No custom link added yet.</small></div>
    </article>''')

def embedded(path):
    return base64.b64encode((ROOT / path).read_bytes()).decode()

prefix = r'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark"><meta name="referrer" content="no-referrer">
<title>lain · Referral programs</title>
<style>
@font-face{font-family:Plex;src:url(data:font/ttf;base64,__FONT__) format('truetype');font-display:swap}
@font-face{font-family:Oxanium;src:url(data:font/ttf;base64,__BRANDFONT__) format('truetype');font-weight:600;font-display:swap}
:root{--bg:#080e0c;--card:#101a16;--line:#2b463a;--mint:#7bd7b1;--ink:#ecf7f1;--muted:#a0b4aa;--amber:#e6bd71;--red:#ed9191}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:17px/1.5 Plex,system-ui,sans-serif}
a{color:var(--mint);text-underline-offset:3px}button,input,select{font:inherit}button,.button{border:1px solid var(--line);background:#13251c;color:var(--ink);padding:9px 13px;border-radius:7px;text-decoration:none;cursor:pointer;white-space:nowrap}button:hover,.button:hover{background:#203d2e;border-color:var(--mint)}button.primary{background:var(--mint);color:#092217;border-color:var(--mint)}:focus-visible{outline:2px solid var(--mint);outline-offset:3px}[hidden]{display:none!important}
.wrap{max-width:1380px;margin:auto;padding:0 28px}.top{border-bottom:1px solid var(--line);background:#0c1510}.top .wrap{display:flex;justify-content:space-between;align-items:center;gap:18px;min-height:80px}.brand{display:flex;align-items:center;gap:13px}.brand img{width:46px;height:46px;object-fit:contain}.wordmark{font:600 25px Oxanium,system-ui}.subbrand,.stamp{font-size:14px;color:var(--muted)}.subbrand{margin-left:9px}
main{padding:40px 0 64px}.eyebrow{text-transform:uppercase;letter-spacing:.15em;color:var(--mint);font-size:13px;margin:0 0 8px}h1{font:600 clamp(32px,4vw,48px)/1.15 Oxanium,system-ui;margin:0 0 14px;letter-spacing:-.025em}.intro{max-width:820px;color:var(--muted);font-size:19px;margin:0}.stats{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin:28px 0}.stat{padding:16px 20px;border:1px solid var(--line);border-radius:9px;background:var(--card)}.stat strong{font:600 30px Oxanium,system-ui;margin-right:12px;color:var(--mint)}.stat:nth-child(2) strong{color:var(--amber)}.stat:nth-child(3) strong{color:var(--red)}.stat span{color:var(--muted)}
.toolbar{display:flex;flex-wrap:wrap;gap:10px;align-items:center;margin:25px 0 10px}.search{flex:1 1 280px}input,select{color:var(--ink);background:#0b1510;border:1px solid var(--line);border-radius:7px;padding:10px 12px;min-width:0}input::placeholder{color:#82998c}.hint{color:var(--muted);font-size:14px}.result{margin:12px 0 16px;color:var(--muted)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,350px),1fr));gap:18px;align-items:start}.provider{border:1px solid var(--line);border-radius:12px;padding:22px;background:var(--card);overflow-wrap:anywhere}.card-head{display:flex;align-items:center;justify-content:space-between;gap:12px}.badge{font-size:13px;padding:3px 9px;border:1px solid var(--line);border-radius:20px;white-space:nowrap}.badge.confirmed{color:var(--mint);background:#173a2a;border-color:#32654b}.badge.unknown{color:var(--amber);background:#30291b;border-color:#5b4930}.badge.ended{color:var(--red);background:#352021;border-color:#704243}.provider-id{font-size:12px;color:var(--muted);text-align:right}.provider h2{font:600 21px/1.25 Oxanium,system-ui;margin:18px 0 12px}.benefit{font-size:17px;line-height:1.55;margin:0 0 19px;color:#d3e6db}details{border-top:1px solid var(--line)}summary{padding:14px 0;color:var(--mint);cursor:pointer;font-weight:600}summary span{float:right;font-weight:400;color:var(--muted);font-size:13px}h3{font-size:15px;margin:20px 0 7px;color:var(--ink)}details p,details li{font-size:15px;color:var(--muted);margin:7px 0}.sources{padding-left:20px}.sources li{margin-bottom:10px}.checked{border-top:1px solid var(--line);padding-top:12px}.own-link{border-top:1px solid var(--line);padding-top:16px;margin-top:2px}.own-link label{display:block;font-size:14px;margin-bottom:7px}.link-row{display:flex;gap:7px}.link-row input{flex:1;width:100%}.saved{display:block;margin-top:6px;color:var(--muted);font-size:12px}.saved.invalid{color:var(--red)}.empty{padding:30px;border:1px dashed var(--line);border-radius:12px;color:var(--muted);grid-column:1/-1}.foot{border-top:1px solid var(--line);margin-top:32px;padding-top:20px;color:var(--muted);font-size:14px}.foot p{max-width:930px}.license{font:12px/1.5 monospace;white-space:pre-wrap;max-height:220px;overflow:auto}.notice{min-height:22px;font-size:14px;color:var(--mint)}
@media(max-width:650px){.wrap{padding:0 16px}.top .wrap{min-height:70px}.subbrand{display:none}.stamp{font-size:12px;text-align:right}.stats{gap:7px}.stat{padding:12px}.stat strong{display:block;margin:0;font-size:25px}.stat span{font-size:13px}.provider{padding:18px}main{padding-top:28px}.toolbar button{flex:1}.link-row{flex-wrap:wrap}.link-row input{flex-basis:100%}}
@media print{body{background:white;color:black}.toolbar,.own-link{display:none}.grid{display:block}.provider{break-inside:avoid;background:white;border-color:#aaa;margin-bottom:16px}.provider p,details p,details li{color:#333}details{display:block}.top,.stat{background:white}a{color:#164932}.badge{color:black!important;background:white!important}}
</style></head><body><header class="top"><div class="wrap"><div class="brand"><img src="data:image/png;base64,__LOGO__" alt="lain cat logo"><div><span class="wordmark">lain</span><span class="subbrand">Local AI Gateway</span></div></div><div class="stamp">RESEARCH NOTE<br>October 24, 2026</div></div></header>
<main><div class="wrap"><p class="eyebrow">Providers · Referrals · Partner programs</p><h1>Which referral links are worthwhile?</h1>
<p class="intro">20 providers from lain, with official sources and clear assessments. Add your own links and save them with the file.</p>
<section class="stats" aria-label="Research results"><div class="stat"><strong>__CONFIRMED__</strong><span>Documented programs</span></div><div class="stat"><strong>__UNKNOWN__</strong><span>Unconfirmed</span></div><div class="stat"><strong>__ENDED__</strong><span>Program ended</span></div></section>
<p class="hint">Confirmed means a publicly documented referral program. Benefits often include credits or membership promotions. This research does not confirm a general cash-paying affiliate program.</p>
<div class="toolbar"><input id="search" class="search" type="search" placeholder="Providers, credits, terms …" aria-label="Search providers"><select id="filter" aria-label="Filter programs"><option value="all">All programs</option><option value="confirmed">Confirmed</option><option value="unknown">Unconfirmed</option><option value="ended">Ended</option><option value="own">With custom link</option></select><button id="save-html" class="primary">Save HTML with links</button><button id="save-json">Export links as JSON</button></div>
<p class="result" id="result" aria-live="polite">20 providers</p><div class="grid" id="providers">'''
suffix = r'''<p class="empty" id="empty" hidden>No providers match this filter. Reset the search or selection.</p></div>
<p id="notice" class="notice" role="status"></p><noscript><p>All research findings and sources are readable without JavaScript. Search and link saving require JavaScript.</p></noscript>
<footer class="foot"><p>This research is a dated snapshot. “Unconfirmed” does not mean that no program exists. General cloud partnerships, startup credits and other product programs do not establish compensation for AI API referrals.</p><p>Your links are stored only in this browser. Use “Save HTML with links” to create a new standalone file containing them. There is no server connection, tracking or externally loaded font. A website opens only when you click a source or referral link.</p>
<details><summary>Fonts &amp; license notices</summary><p>IBM Plex Sans Condensed and Oxanium are embedded locally. Original SIL Open Font Licenses:</p><pre class="license">__LICENSES__</pre></details></footer>
</div></main><script id="saved-links" type="application/json">{}</script>
<script>
(function(){
  'use strict';
  var key='lain-referral-links-v1', links={}, storage=true;
  var cards=Array.from(document.querySelectorAll('.provider'));
  var notice=document.getElementById('notice');
  function parse(value){try{var obj=JSON.parse(value||'{}');return obj && typeof obj==='object' && !Array.isArray(obj)?obj:{};}catch(e){return {};}}
  function safe(value){try{var url=new URL(value);return (url.protocol==='https:'||url.protocol==='http:')&&!url.username&&!url.password?url.href:null;}catch(e){return null;}}
  var embedded=parse(document.getElementById('saved-links').textContent), local={};
  try{local=parse(localStorage.getItem(key));}catch(e){storage=false;}
  cards.forEach(function(card){var id=card.dataset.id;var value=Object.prototype.hasOwnProperty.call(local,id)?local[id]:(embedded[id]||'');if(typeof value==='string'&&safe(value))links[id]=value;});
  function filter(){var needle=document.getElementById('search').value.toLocaleLowerCase('de').trim();var kind=document.getElementById('filter').value;var count=0;
    cards.forEach(function(card){var match=(!needle||card.dataset.search.indexOf(needle)!==-1)&&(kind==='all'||(kind==='own'?Boolean(links[card.dataset.id]):card.dataset.state===kind));card.hidden=!match;if(match)count++;});
    document.getElementById('result').textContent=count+' of '+cards.length+' providers';document.getElementById('empty').hidden=count!==0;
  }
  cards.forEach(function(card){var id=card.dataset.id,input=card.querySelector('input'),status=card.querySelector('.saved'),open=card.querySelector('.own-open');input.value=links[id]||'';
    function update(save){var raw=input.value.trim(),url=raw?safe(raw):null;status.classList.toggle('invalid',Boolean(raw&&!url));open.hidden=!url;if(url)open.href=url;else open.removeAttribute('href');
      if(save){if(url)links[id]=raw;else delete links[id];local[id]=url?raw:'';try{localStorage.setItem(key,JSON.stringify(local));}catch(e){storage=false;notice.textContent='Browser storage is unavailable. Export HTML to preserve your links.';}filter();}
      status.textContent=!raw?'No custom link added yet.':!url?'Enter a complete HTTP/HTTPS link without credentials.':storage?'Saved in this browser. HTML export includes the link.':'Saved for this session only. Please export HTML.';
    }
    input.addEventListener('input',function(){update(true);});update(false);
  });
  document.getElementById('search').addEventListener('input',filter);document.getElementById('filter').addEventListener('change',filter);
  function download(content,type,name){var url=URL.createObjectURL(new Blob([content],{type:type})),a=document.createElement('a');a.href=url;a.download=name;document.body.appendChild(a);a.click();a.remove();setTimeout(function(){URL.revokeObjectURL(url);},1000);}
  document.getElementById('save-json').addEventListener('click',function(){download(JSON.stringify(links,null,2)+'\n','application/json','lain-referral-links.json');notice.textContent='Exported links as JSON.';});
  document.getElementById('save-html').addEventListener('click',function(){var copy=document.documentElement.cloneNode(true);copy.querySelector('#saved-links').textContent=JSON.stringify(links).replace(/</g,'\\u003c');copy.querySelector('#search').setAttribute('value','');copy.querySelector('#filter').selectedIndex=0;copy.querySelectorAll('.provider').forEach(function(card){card.hidden=false;});copy.querySelector('#empty').hidden=true;copy.querySelector('#notice').textContent='';download('<!doctype html>\n'+copy.outerHTML,'text/html','lain-referrals.html');notice.textContent='Exported a standalone HTML file with your links.';});
  filter();
})();
</script></body></html>'''

prefix = prefix.replace('__FONT__', embedded('src/Lain.Desktop/Assets/Fonts/IBMPlexSansCondensed/IBMPlexSansCondensed-Regular.ttf'))
prefix = prefix.replace('__BRANDFONT__', embedded('src/Lain.Desktop/Assets/Fonts/Oxanium/Oxanium-SemiBold.ttf'))
prefix = prefix.replace('__LOGO__', embedded('src/Lain.Desktop/Assets/Brand/lain-cat.png'))
for category in ('confirmed', 'unknown', 'ended'):
    prefix = prefix.replace('__' + category.upper() + '__', str(sum(state(row)[0] == category for row in rows)))
licenses = '\n\n'.join((ROOT / f'src/Lain.Desktop/Assets/Fonts/{family}/OFL.txt').read_text() for family in ('IBMPlexSansCondensed', 'Oxanium'))
licenses = '\n'.join(line.rstrip() for line in licenses.splitlines())
suffix = suffix.replace('__LICENSES__', escape(licenses))
target = ROOT / 'docs/provider-referrals.html'
target.write_text(prefix + ''.join(cards) + suffix)
print(f'{target}: {len(rows)} providers, all resources embedded')
