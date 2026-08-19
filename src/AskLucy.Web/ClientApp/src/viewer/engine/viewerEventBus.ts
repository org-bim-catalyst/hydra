import type { ViewerEvent, ViewerEventHandler, ViewerEventType } from '../api/events'

type AnyHandler = (event: ViewerEvent) => void

/** Simple typed pub/sub for viewer state-change notifications (FR-023, contracts/viewer-engine-api.md).
 * `emit` never throws — a handler that errors would otherwise break every other subscriber and the
 * command that triggered it. */
export class ViewerEventBus {
  private readonly handlers = new Map<ViewerEventType, Set<AnyHandler>>()

  on<E extends ViewerEventType>(type: E, handler: ViewerEventHandler<E>): () => void {
    let set = this.handlers.get(type)
    if (!set) {
      set = new Set()
      this.handlers.set(type, set)
    }
    set.add(handler as AnyHandler)
    return () => this.off(type, handler)
  }

  off<E extends ViewerEventType>(type: E, handler: ViewerEventHandler<E>): void {
    this.handlers.get(type)?.delete(handler as AnyHandler)
  }

  emit(event: ViewerEvent): void {
    const set = this.handlers.get(event.type)
    if (!set) return
    for (const handler of set) {
      handler(event)
    }
  }
}
