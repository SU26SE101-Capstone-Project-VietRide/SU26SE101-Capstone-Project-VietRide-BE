import { Injectable } from '@nestjs/common';

@Injectable()
export class RouteStateGenerationRegistry {
  private readonly generations = new Map<string, number>();

  capture(tripId: string): number {
    return this.generations.get(tripId) ?? 0;
  }

  invalidate(tripId: string): number {
    const nextGeneration = this.capture(tripId) + 1;
    this.generations.set(tripId, nextGeneration);
    return nextGeneration;
  }

  isCurrent(tripId: string, generation: number): boolean {
    return this.capture(tripId) === generation;
  }
}
