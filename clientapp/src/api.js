const base = '/api/v1'
async function req(path, opts = {}) {
  const res = await fetch(base + path, {
    headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin',
    ...opts, body: opts.body ? JSON.stringify(opts.body) : undefined
  })
  const text = await res.text(); const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new Error(data?.error || `Lỗi ${res.status}`)
  return { data, cache: res.headers.get('X-Cache') }
}
export const api = {
  dashboard: () => req('/dashboard'),
  templates: () => req('/templates'),
  saveTemplate: (b) => req('/templates', { method: 'POST', body: b }),
  notifications: (status, channel) => req(`/notifications?${status != null ? `status=${status}&` : ''}${channel != null ? `channel=${channel}` : ''}`),
  send: (b) => req('/send', { method: 'POST', body: b }),
  sendTemplate: (b) => req('/send-template', { method: 'POST', body: b }),
  retry: (id) => req(`/notifications/${id}/retry`, { method: 'POST' }),
  delivered: (id) => req(`/notifications/${id}/delivered`, { method: 'POST' })
}
export const fmtDateTime = (s) => s ? new Date(s).toLocaleString('vi-VN') : '—'
export const CHANNELS = ['Email', 'SMS', 'Zalo', 'Push']
export const NSTATUS = ['Chờ gửi', 'Đã gửi', 'Đã nhận', 'Thất bại']
