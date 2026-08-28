import React, { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Outlet } from 'react-router-dom'
import { api, fmtDateTime, CHANNELS, NSTATUS } from './api'

function Badge({ text, css }) { return <span className={`badge ${css || 'secondary'}`}>{text}</span> }
function Flash({ msg }) { return msg ? <div className={`flash ${msg.ok ? 'ok' : 'err'}`}>{msg.text}</div> : null }
function Modal({ title, onClose, wide, children }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" style={wide ? { maxWidth: 640 } : undefined} onClick={e => e.stopPropagation()}>
        <div className="row" style={{ marginBottom: 12 }}><h2 style={{ flex: 1, margin: 0 }}>{title}</h2>
          <button className="btn gray sm" style={{ flex: 'none' }} onClick={onClose}>Đóng</button></div>{children}
      </div>
    </div>
  )
}
function Field({ label, children }) { return <div style={{ flex: 1 }}><label>{label}</label>{children}</div> }

function Layout() {
  return (
    <>
      <nav className="nav"><span className="brand">🔔 MiniNotify</span>
        <NavLink to="/" end>Tổng quan</NavLink><NavLink to="/notifications">Thông báo</NavLink>
        <NavLink to="/templates">Mẫu</NavLink><NavLink to="/send">Gửi</NavLink></nav>
      <div className="wrap"><Outlet /></div>
    </>
  )
}

function Dashboard() {
  const [d, setD] = useState(null); const [cache, setCache] = useState('')
  useEffect(() => { api.dashboard().then(r => { setD(r.data); setCache(r.cache) }) }, [])
  if (!d) return <p className="muted">Đang tải…</p>
  const max = Math.max(1, ...d.byChannel.map(x => x.count))
  return (
    <>
      <h1>Tổng quan thông báo {cache && <span className="pill">cache: {cache}</span>}</h1>
      <div className="grid kpis" style={{ marginBottom: 18 }}>
        <div className="kpi"><div className="v">{d.total}</div><div className="l">Tổng gửi</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--info)' }}>{d.sent}</div><div className="l">Đã gửi</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--success)' }}>{d.delivered}</div><div className="l">Đã nhận</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--danger)' }}>{d.failed}</div><div className="l">Thất bại</div></div>
      </div>
      <div className="card funnel"><h2>Theo kênh</h2>
        {d.byChannel.map((x, i) => (<div className="bar" key={i}><div className="lbl">{x.channel}</div>
          <div className="track"><div className="fill" style={{ width: `${(x.count / max) * 100}%` }} /></div><div className="n">{x.count}</div></div>))}
      </div>
    </>
  )
}

function Notifications() {
  const [rows, setRows] = useState([]); const [status, setStatus] = useState(''); const [channel, setChannel] = useState(''); const [msg, setMsg] = useState(null)
  const load = () => api.notifications(status === '' ? null : Number(status), channel === '' ? null : Number(channel)).then(r => setRows(r.data))
  useEffect(() => { load() }, [status, channel])
  const act = async (fn) => { try { const r = await fn(); setMsg({ ok: true, text: r.data.msg }); load() } catch (e) { setMsg({ ok: false, text: e.message }) } }
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 'none' }}>Thông báo</h1><div className="sp" />
        <select style={{ maxWidth: 130 }} value={channel} onChange={e => setChannel(e.target.value)}><option value="">— Kênh —</option>{CHANNELS.map((c, i) => <option key={i} value={i}>{c}</option>)}</select>
        <select style={{ maxWidth: 130 }} value={status} onChange={e => setStatus(e.target.value)}><option value="">— Trạng thái —</option>{NSTATUS.map((s, i) => <option key={i} value={i}>{s}</option>)}</select></div>
      <Flash msg={msg} />
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Kênh</th><th>Đến</th><th>Tiêu đề/Nội dung</th><th>Nhà cung cấp</th><th>Trạng thái</th><th></th></tr></thead>
          <tbody>{rows.map(n => (
            <tr key={n.id}><td><span className="pill">{n.channelText}</span></td><td>{n.toAddress}</td><td style={{ maxWidth: 280 }}>{n.subject ? <b>{n.subject}</b> : ''} <span className="muted">{n.body?.slice(0, 60)}</span></td>
              <td className="muted">{n.provider || '—'}{n.retryCount > 0 ? ` (retry ${n.retryCount})` : ''}</td><td><Badge text={n.statusText} css={n.statusCss} /></td>
              <td className="right">{n.status === 3 && <button className="btn sm" style={{ flex: 'none' }} onClick={() => act(() => api.retry(n.id))}>Gửi lại</button>}
                {n.status === 1 && <button className="btn ghost sm" style={{ flex: 'none' }} onClick={() => act(() => api.delivered(n.id))}>Đã nhận</button>}</td></tr>))}
            {rows.length === 0 && <tr><td colSpan={6} className="muted" style={{ padding: 20 }}>Chưa có thông báo.</td></tr>}</tbody></table>
      </div>
    </>
  )
}

function Templates() {
  const [rows, setRows] = useState([]); const [edit, setEdit] = useState(null)
  const load = () => api.templates().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 1 }}>Mẫu thông báo</h1><button className="btn sm" style={{ flex: 'none' }} onClick={() => setEdit({ id: 0, code: '', name: '', channel: 0, subject: '', body: '', active: true })}>+ Thêm mẫu</button></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Mã</th><th>Tên</th><th>Kênh</th><th>Tiêu đề</th><th></th></tr></thead>
          <tbody>{rows.map(t => (<tr key={t.id}><td>{t.code}</td><td>{t.name}</td><td><span className="pill">{t.channelText}</span></td><td>{t.subject || '—'}</td>
            <td className="right"><button className="btn ghost sm" style={{ flex: 'none' }} onClick={() => setEdit(t)}>Sửa</button></td></tr>))}
            {rows.length === 0 && <tr><td colSpan={5} className="muted" style={{ padding: 20 }}>Chưa có mẫu.</td></tr>}</tbody></table>
      </div>
      {edit && <TemplateForm t={edit} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); load() }} />}
    </>
  )
}

function TemplateForm({ t, onClose, onSaved }) {
  const [f, setF] = useState({ ...t }); const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => { try { if (!f.name) { setErr('Cần tên'); return } await api.saveTemplate({ ...f, channel: Number(f.channel) }); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title={f.id ? 'Sửa mẫu' : 'Thêm mẫu'} onClose={onClose} wide>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Mã"><input value={f.code} onChange={e => up('code', e.target.value)} /></Field>
        <Field label="Tên *"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field>
        <Field label="Kênh"><select value={f.channel} onChange={e => up('channel', e.target.value)}>{CHANNELS.map((c, i) => <option key={i} value={i}>{c}</option>)}</select></Field></div>
      <Field label="Tiêu đề (Email/Push)"><input value={f.subject} onChange={e => up('subject', e.target.value)} /></Field>
      <Field label="Nội dung (dùng {{ten_bien}})"><textarea rows={4} value={f.body} onChange={e => up('body', e.target.value)} /></Field>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Lưu</button></div>
    </Modal>
  )
}

function Send() {
  const [tab, setTab] = useState('direct')
  const [d, setD] = useState({ channel: 0, to: '', subject: '', body: '' })
  const [t, setT] = useState({ code: '', to: '', dataText: '{"ten":"Anh Nam"}' })
  const [msg, setMsg] = useState(null)
  const sendDirect = async () => { try { const r = await api.send({ ...d, channel: Number(d.channel) }); setMsg({ ok: true, text: `Đã gửi (id ${r.data.id}, ${r.data.status})` }) } catch (e) { setMsg({ ok: false, text: e.message }) } }
  const sendTpl = async () => { try { const data = JSON.parse(t.dataText || '{}'); const r = await api.sendTemplate({ code: t.code, to: t.to, data }); setMsg({ ok: true, text: `Đã gửi (id ${r.data.id}, ${r.data.status})` }) } catch (e) { setMsg({ ok: false, text: e.message }) } }
  return (
    <>
      <h1>Gửi thông báo</h1>
      <div className="row" style={{ marginBottom: 12 }}>
        <button className={`btn ${tab === 'direct' ? '' : 'gray'} sm`} style={{ flex: 'none' }} onClick={() => setTab('direct')}>Gửi trực tiếp</button>
        <button className={`btn ${tab === 'tpl' ? '' : 'gray'} sm`} style={{ flex: 'none' }} onClick={() => setTab('tpl')}>Gửi theo mẫu</button>
      </div>
      <Flash msg={msg} />
      {tab === 'direct' ? (
        <div className="card">
          <div className="row"><Field label="Kênh"><select value={d.channel} onChange={e => setD({ ...d, channel: e.target.value })}>{CHANNELS.map((c, i) => <option key={i} value={i}>{c}</option>)}</select></Field>
            <Field label="Đến (email/SĐT/token)"><input value={d.to} onChange={e => setD({ ...d, to: e.target.value })} /></Field></div>
          <Field label="Tiêu đề"><input value={d.subject} onChange={e => setD({ ...d, subject: e.target.value })} /></Field>
          <Field label="Nội dung"><textarea rows={3} value={d.body} onChange={e => setD({ ...d, body: e.target.value })} /></Field>
          <div style={{ marginTop: 12 }}><button className="btn" onClick={sendDirect}>Gửi</button></div>
        </div>
      ) : (
        <div className="card">
          <div className="row"><Field label="Mã mẫu"><input value={t.code} onChange={e => setT({ ...t, code: e.target.value })} /></Field>
            <Field label="Đến"><input value={t.to} onChange={e => setT({ ...t, to: e.target.value })} /></Field></div>
          <Field label="Dữ liệu placeholder (JSON)"><textarea rows={3} value={t.dataText} onChange={e => setT({ ...t, dataText: e.target.value })} /></Field>
          <div style={{ marginTop: 12 }}><button className="btn" onClick={sendTpl}>Gửi theo mẫu</button></div>
        </div>
      )}
    </>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="notifications" element={<Notifications />} />
        <Route path="templates" element={<Templates />} />
        <Route path="send" element={<Send />} />
      </Route>
    </Routes>
  )
}
