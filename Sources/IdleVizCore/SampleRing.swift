import Foundation
import os

/// Keeps the most recent stereo samples from the Spotify tap. Written on the Core Audio
/// I/O queue and read on the main thread, so everything goes through one lock.
public final class SampleRing: Sendable {
    public static let capacity = 4096

    private struct State {
        var left = [Float](repeating: 0, count: SampleRing.capacity)
        var right = [Float](repeating: 0, count: SampleRing.capacity)
        var head = 0
        var buffers = 0
        var zeroBuffers = 0
    }

    private let state = OSAllocatedUnfairLock(initialState: State())

    public init() {}

    /// Adds one buffer of interleaved samples. Mono is copied to both sides; channels past the second are ignored.
    public func append(interleaved samples: UnsafePointer<Float>, channels: Int, frames: Int) {
        let channels = max(channels, 1)
        state.withLockUnchecked { state in
            var allZero = true
            for frame in 0..<frames {
                let left = samples[frame * channels]
                let right = channels > 1 ? samples[frame * channels + 1] : left
                if left != 0 || right != 0 { allZero = false }
                state.left[state.head] = left
                state.right[state.head] = right
                state.head = (state.head + 1) % Self.capacity
            }
            state.buffers += 1
            if allZero { state.zeroBuffers += 1 }
        }
    }

    /// Adds one buffer that has a separate array per channel.
    public func append(left: UnsafePointer<Float>, right: UnsafePointer<Float>, frames: Int) {
        state.withLockUnchecked { state in
            var allZero = true
            for frame in 0..<frames {
                if left[frame] != 0 || right[frame] != 0 { allZero = false }
                state.left[state.head] = left[frame]
                state.right[state.head] = right[frame]
                state.head = (state.head + 1) % Self.capacity
            }
            state.buffers += 1
            if allZero { state.zeroBuffers += 1 }
        }
    }

    /// Copies the newest `left.count` samples of each channel, oldest first.
    public func latest(left: inout [Float], right: inout [Float]) {
        let count = min(left.count, right.count, Self.capacity)
        state.withLockUnchecked { state in
            var index = (state.head - count + Self.capacity) % Self.capacity
            for offset in 0..<count {
                left[offset] = state.left[index]
                right[offset] = state.right[index]
                index = (index + 1) % Self.capacity
            }
        }
    }

    /// Without this, frames built after the tap stops would repeat the last samples forever.
    public func clear() {
        state.withLockUnchecked { state in
            state.left = [Float](repeating: 0, count: Self.capacity)
            state.right = [Float](repeating: 0, count: Self.capacity)
        }
    }

    /// Buffers received since the last call, and how many of them held only exact zeros.
    public func takeCounts() -> (buffers: Int, zero: Int) {
        state.withLockUnchecked { state in
            defer {
                state.buffers = 0
                state.zeroBuffers = 0
            }
            return (state.buffers, state.zeroBuffers)
        }
    }
}
