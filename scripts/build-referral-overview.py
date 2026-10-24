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
        return 'ended', 'Beendet'
    if row['status'] in ('public_referral', 'public_affiliate'):
        return 'confirmed', 'Referral bestätigt' if row['status'] == 'public_referral' else 'Affiliate bestätigt'
    return 'unknown', 'Nicht bestätigt'

def paragraphs(value):
    return ''.join('<p>' + escape(item) + '</p>' for item in (value if isinstance(value, list) else [value]) if item)

rows.sort(key=lambda row: ({'confirmed': 0, 'unknown': 1, 'ended': 2}[state(row)[0]], row['provider'].casefold()))
cards = []
for row in rows:
    category, label = state(row)
    benefit = row['benefit'] or ('Das Programm ist beendet. Hier keinen aktiven Referral-Link bewerben.' if category == 'ended'
        else 'Programm belegt; aktuelle Prämienbedingungen noch nicht bestätigt.' if category == 'confirmed'
        else 'Ein öffentliches Referral-/Affiliateprogramm für dieses Produkt ist nicht belegt.')
    source_links = []
    for source in row['officialSources']:
        assert source['url'].startswith('https://')
        source_links.append('<li><a href="' + escape(source['url']) + '" target="_blank" rel="noopener noreferrer">' + escape(source['label']) + ' ↗</a></li>')
    search = json.dumps(row, ensure_ascii=False).lower()
    cards.append(f'''<article class="provider" data-id="{escape(row['id'])}" data-state="{category}" data-search="{escape(search)}">
      <div class="card-head"><span class="badge {category}">{label}</span><span class="provider-id">{escape(row['id'])}</span></div>
      <h2>{escape(row['provider'])}</h2><p class="benefit">{escape(benefit)}</p>
      <details><summary>Bedingungen &amp; Quellen <span>{len(source_links)} Quellen</span></summary>
        <h3>Wer kann teilnehmen?</h3>{paragraphs(row['eligibility'])}
        <h3>Produktbezug &amp; Grenzen</h3>{paragraphs(row['limitations'])}
        <h3>Offizielle Quellen</h3><ul class="sources">{''.join(source_links)}</ul>
        <h3>Einordnung</h3>{paragraphs(row['notes'])}<p class="checked">Geprüft am 24. Oktober 2026</p>
      </details>
      <div class="own-link"><label for="link-{escape(row['id'])}">Eigener Referral-Link</label><div class="link-row">
        <input id="link-{escape(row['id'])}" type="url" autocomplete="off" spellcheck="false" placeholder="https://…" aria-describedby="saved-{escape(row['id'])}">
        <a class="button own-open" hidden target="_blank" rel="noopener noreferrer">Öffnen ↗</a></div>
        <small id="saved-{escape(row['id'])}" class="saved">Noch kein eigener Link eingetragen.</small></div>
    </article>''')

def embedded(path):
    return base64.b64encode((ROOT / path).read_bytes()).decode()

prefix = r'''<!doctype html>
<html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark"><meta name="referrer" content="no-referrer">
<title>lain · Referral-Programme</title>
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
</style></head><body><header class="top"><div class="wrap"><div class="brand"><img src="data:image/png;base64,__LOGO__" alt="lain Katzenlogo"><div><span class="wordmark">lain</span><span class="subbrand">Local AI Gateway</span></div></div><div class="stamp">RESEARCH NOTE<br>24. Oktober 2026</div></div></header>
<main><div class="wrap"><p class="eyebrow">Anbieter · Empfehlungen · Partnerprogramme</p><h1>Wo lohnt sich ein Referral-Link?</h1>
<p class="intro">20 Anbieter aus lain, mit offiziellen Quellen und klarer Einordnung. Eigene Links kannst du direkt eintragen und mit der Datei speichern.</p>
<section class="stats" aria-label="Rechercheergebnis"><div class="stat"><strong>__CONFIRMED__</strong><span>Programme belegt</span></div><div class="stat"><strong>__UNKNOWN__</strong><span>Nicht bestätigt</span></div><div class="stat"><strong>__ENDED__</strong><span>Programm beendet</span></div></section>
<p class="hint">Bestätigt bedeutet: ein öffentlich belegtes Empfehlungsprogramm. Vorteile sind oft Credits oder Mitgliedschaftsaktionen. Ein allgemeines Barauszahlungs-Affiliateprogramm ist in dieser Recherche nicht bestätigt.</p>
<div class="toolbar"><input id="search" class="search" type="search" placeholder="Anbieter, Credits, Bedingungen …" aria-label="Anbieter durchsuchen"><select id="filter" aria-label="Programme filtern"><option value="all">Alle Programme</option><option value="confirmed">Bestätigt</option><option value="unknown">Nicht bestätigt</option><option value="ended">Beendet</option><option value="own">Mit eigenem Link</option></select><button id="save-html" class="primary">HTML mit Links speichern</button><button id="save-json">Links als JSON</button></div>
<p class="result" id="result" aria-live="polite">20 Anbieter</p><div class="grid" id="providers">'''
suffix = r'''<p class="empty" id="empty" hidden>Keine Anbieter für diesen Filter. Suche oder Auswahl zurücksetzen.</p></div>
<p id="notice" class="notice" role="status"></p><noscript><p>Alle Rechercheergebnisse und Quellen sind ohne JavaScript lesbar. Suche und Link-Speicherung benötigen JavaScript.</p></noscript>
<footer class="foot"><p>Die Recherche ist eine datierte Momentaufnahme. „Nicht bestätigt“ bedeutet nicht, dass kein Programm existiert. Allgemeine Cloud-Partnerschaften, Startup-Credits und andere Produktprogramme sind kein Beleg für die Vergütung von AI-API-Empfehlungen.</p><p>Eigene Links werden nur in diesem Browser gespeichert. Mit „HTML mit Links speichern“ erzeugst du eine neue eigenständige Datei, die sie mitnimmt. Es gibt keine Serveranbindung, Tracker oder extern geladenen Schriften. Erst ein angeklickter Quellen- oder Referral-Link öffnet die jeweilige Website.</p>
<details><summary>Schriften &amp; Lizenzhinweise</summary><p>IBM Plex Sans Condensed und Oxanium sind lokal eingebettet. Originale SIL-Open-Font-Lizenzen:</p><pre class="license">__LICENSES__</pre></details></footer>
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
    document.getElementById('result').textContent=count+' von '+cards.length+' Anbietern';document.getElementById('empty').hidden=count!==0;
  }
  cards.forEach(function(card){var id=card.dataset.id,input=card.querySelector('input'),status=card.querySelector('.saved'),open=card.querySelector('.own-open');input.value=links[id]||'';
    function update(save){var raw=input.value.trim(),url=raw?safe(raw):null;status.classList.toggle('invalid',Boolean(raw&&!url));open.hidden=!url;if(url)open.href=url;else open.removeAttribute('href');
      if(save){if(url)links[id]=raw;else delete links[id];local[id]=url?raw:'';try{localStorage.setItem(key,JSON.stringify(local));}catch(e){storage=false;notice.textContent='Browser-Speicherung nicht verfügbar. Mit dem HTML-Export bleiben deine Links erhalten.';}filter();}
      status.textContent=!raw?'Noch kein eigener Link eingetragen.':!url?'Bitte einen vollständigen HTTP-/HTTPS-Link ohne Zugangsdaten eingeben.':storage?'Im Browser gespeichert. HTML-Export nimmt den Link mit.':'Nur für diese Sitzung gespeichert. Bitte HTML exportieren.';
    }
    input.addEventListener('input',function(){update(true);});update(false);
  });
  document.getElementById('search').addEventListener('input',filter);document.getElementById('filter').addEventListener('change',filter);
  function download(content,type,name){var url=URL.createObjectURL(new Blob([content],{type:type})),a=document.createElement('a');a.href=url;a.download=name;document.body.appendChild(a);a.click();a.remove();setTimeout(function(){URL.revokeObjectURL(url);},1000);}
  document.getElementById('save-json').addEventListener('click',function(){download(JSON.stringify(links,null,2)+'\n','application/json','lain-referral-links.json');notice.textContent='Linkliste als JSON exportiert.';});
  document.getElementById('save-html').addEventListener('click',function(){var copy=document.documentElement.cloneNode(true);copy.querySelector('#saved-links').textContent=JSON.stringify(links).replace(/</g,'\\u003c');copy.querySelector('#search').setAttribute('value','');copy.querySelector('#filter').selectedIndex=0;copy.querySelectorAll('.provider').forEach(function(card){card.hidden=false;});copy.querySelector('#empty').hidden=true;copy.querySelector('#notice').textContent='';download('<!doctype html>\n'+copy.outerHTML,'text/html','lain-referrals.html');notice.textContent='Eigenständige HTML-Datei mit deinen Links exportiert.';});
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
