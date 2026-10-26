// Render within the existing menu using its native items and selection callback.
// React and the menu primitives are provided by the pinned upstream bundle.
const hint = 'Lain-Gruppen nicht verfügbar · native Modellauswahl';

export function groupedRows({jsx, React, Menu, options, renderOption}) {
  const metadata = options.find(option => option._lainGroups)?._lainGroups;
  if (!metadata) return options.map(renderOption);
  return jsx(LainRows, {jsx, React, Menu, options, renderOption, metadata});
}

export function LainRows({jsx, React, Menu, options, renderOption, metadata}) {
  const [verification, setVerification] = React.useState(null);
  const [selection, setSelection] = React.useState(null);
  const root = React.useRef(null), scroll = React.useRef(new Map());
  const checked = verification?.metadata === metadata ? verification.result : null;
  const groupId = selection != null && checked?.status === 'ready' &&
    selection.generation === checked.generation ? selection.id : null;
  const scroller = () => {
    let node = root.current?.parentElement;
    while (node) {
      if (/(auto|scroll)/.test(getComputedStyle(node).overflowY)) return node;
      node = node.parentElement;
    }
    return root.current?.parentElement;
  };
  const focus = (id, view) => requestAnimationFrame(() => {
    const element = root.current;
    if (!element) return;
    const target = id == null ? element.querySelector('[role^="menuitem"]') :
      [...element.querySelectorAll('[data-lain-group-id]')].find(node => node.dataset.lainGroupId === id);
    target?.focus();
    const parent = scroller();
    if (parent) parent.scrollTop = scroll.current.get(view) ?? 0;
  });
  const navigate = (id) => {
    const parent = scroller();
    if (parent) scroll.current.set(groupId ?? '__groups', parent.scrollTop);
    setSelection(id == null ? null : {id, generation: checked.generation});
    focus(id == null ? groupId : null, id ?? '__groups');
  };
  React.useEffect(() => {
    let cancelled = false, running = false;
    const controller = new AbortController();
    setVerification(null);
    const refresh = async () => {
      if (running) return;
      running = true;
      try {
        const next = await metadata.verify(controller.signal);
        if (!cancelled) setVerification({metadata, result: next});
      } catch {
        if (!cancelled) setVerification({metadata, result: {status: 'native', reason: 'unavailable'}});
      } finally { running = false; }
    };
    refresh();
    const timer = setInterval(refresh, 5000);
    return () => { cancelled = true; controller.abort(); clearInterval(timer); };
  }, [metadata]);
  React.useEffect(() => {
    if (groupId == null) return;
    const onKey = (event) => {
      if (event.key === 'Escape' && root.current?.isConnected && !root.current.closest('[aria-hidden="true"]')) {
        event.preventDefault(); event.stopImmediatePropagation(); navigate(null);
      }
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [groupId, checked?.generation]);

  if (checked?.status !== 'ready') {
    return jsx('div', {ref: root, 'data-lain-groups': checked == null ? 'checking' : 'native', children: [
      jsx('div', {role: 'status', className: 'px-3 py-1 text-xs text-token-description-foreground', children: checked == null ? 'Lain-Gruppen werden geprüft …' : hint}, 'hint'),
      ...options.map(renderOption)
    ]});
  }
  const available = new Map(options.map(option => [option.id, option]));
  const groups = [...checked.groups.map(group => ({...group, key: `group:${group.id}`})),
    {key: 'all:', name: 'Alle', models: [...available.keys()], all: true}];
  const group = groups.find(group => group.key === groupId);
  let rows;
  if (!group) {
    rows = groups.map(group => jsx(Menu.Item, {
      'data-lain-group-id': group.key,
      'aria-label': group.name,
      SubText: `${group.models.filter(id => available.has(id)).length} Modelle`,
      RightIcon: group.models.some(id => available.get(id)?.selected) || available.get(group.router_model)?.selected ? () => '✓' : () => '›',
      onSelect: event => {event.preventDefault(); navigate(group.key);},
      children: group.name
    }, group.key));
  } else {
    rows = [jsx(Menu.Item, {'aria-label': 'Zurück zu Gruppen', onSelect: event => {event.preventDefault(); navigate(null);}, children: '← Gruppen'}, '__back')];
    rows.push(jsx('div', {className: 'px-3 py-1 text-xs text-token-description-foreground', children: group.name}, '__title'));
    if (!group.all) {
      const router = available.get(group.router_model);
      rows.push(router ? renderOption({...router, label: 'Automatisch'}) : jsx(Menu.Item, {
        disabled: true, SubText: 'Kein verfügbarer Gruppenrouter', children: 'Automatisch'
      }, '__router'));
    }
    const ids = new Set(group.models);
    if (!group.all) ids.delete(group.router_model);
    rows.push(...[...ids].map(id => available.get(id)).filter(Boolean).map(renderOption));
  }
  return jsx('div', {ref: root, 'data-lain-groups': group ? 'models' : 'groups',
    'data-lain-generation': checked.generation, style: {minWidth: 0, maxWidth: '100%', overflowWrap: 'anywhere'}, children: rows});
}
