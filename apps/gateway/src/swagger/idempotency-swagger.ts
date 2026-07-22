type OpenApiOperation = Record<string, unknown>;
type OpenApiParameter = Record<string, unknown>;

interface ImmutableValue {
  get(key: string): unknown;
}

interface ImmutableList {
  find(predicate: (value: ImmutableValue) => boolean): ImmutableValue | undefined;
}

interface ExecuteSpecActions {
  changeParamByIdentity(
    pathMethod: [string, string],
    parameter: ImmutableValue,
    value: string,
  ): void;
  clearResponse(path: string, method: string): void;
}

interface ExecuteComponentProps {
  operation: ImmutableValue;
  path: string;
  method: string;
  specActions: ExecuteSpecActions;
}

interface ReactLike {
  createElement(type: unknown, props?: unknown, ...children: unknown[]): unknown;
}

interface SwaggerPluginSystem {
  React: ReactLike;
}

type ExecuteComponent = (props: ExecuteComponentProps) => unknown;

interface SwaggerPluginDefinition {
  wrapComponents: {
    execute(original: ExecuteComponent): ExecuteComponent;
  };
}

export function idempotencyParameterMacro(
  operation: OpenApiOperation | null | undefined,
  parameter: OpenApiParameter,
): string | undefined {
  const extension = operation?.['x-vietride-idempotency'] as Record<string, unknown> | undefined;
  const isRequired = extension?.['required'] === true;
  const isIdempotencyHeader =
    parameter['in'] === 'header' &&
    typeof parameter['name'] === 'string' &&
    parameter['name'].toLowerCase() === 'idempotency-key';

  return isRequired && isIdempotencyHeader ? globalThis.crypto.randomUUID() : undefined;
}

export function vietRideIdempotencySwaggerPlugin(
  system: SwaggerPluginSystem,
): SwaggerPluginDefinition {
  function read(value: unknown, key: string): unknown {
    if (value && typeof (value as ImmutableValue).get === 'function') {
      return (value as ImmutableValue).get(key);
    }
    return value && typeof value === 'object' ? (value as Record<string, unknown>)[key] : undefined;
  }

  function isRequired(operation: ImmutableValue): boolean {
    return read(read(operation, 'x-vietride-idempotency'), 'required') === true;
  }

  function findIdempotencyParameter(operation: ImmutableValue): ImmutableValue | undefined {
    const parameters = read(operation, 'parameters') as ImmutableList | undefined;
    return parameters?.find(
      (parameter) =>
        read(parameter, 'in') === 'header' &&
        String(read(parameter, 'name')).toLowerCase() === 'idempotency-key',
    );
  }

  return {
    wrapComponents: {
      execute:
        (Original: ExecuteComponent): ExecuteComponent =>
        (props: ExecuteComponentProps): unknown => {
          const originalElement = system.React.createElement(Original, props);
          if (!isRequired(props.operation)) {
            return originalElement;
          }

          const parameter = findIdempotencyParameter(props.operation);
          if (!parameter) {
            return originalElement;
          }

          const rotateKey = (): void => {
            props.specActions.changeParamByIdentity(
              [props.path, props.method],
              parameter,
              globalThis.crypto.randomUUID(),
            );
            props.specActions.clearResponse(props.path, props.method);
          };

          const newOperationButton = system.React.createElement(
            'button',
            {
              type: 'button',
              className: 'btn opblock-control__btn vietride-new-operation',
              onClick: rotateKey,
              title: 'Generate a new UUID v4 for a new logical operation.',
            },
            'New operation',
          );

          return system.React.createElement(
            'div',
            { className: 'execute-wrapper vietride-idempotency-controls' },
            originalElement,
            newOperationButton,
          );
        },
    },
  };
}
