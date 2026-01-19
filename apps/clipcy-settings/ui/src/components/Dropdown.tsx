import { Component, createSignal, For, Show } from 'solid-js';

interface DropdownOption {
  value: string;
  label: string;
}

interface DropdownProps {
  value: string;
  options: DropdownOption[];
  onChange: (value: string) => void;
}

const Dropdown: Component<DropdownProps> = (props) => {
  const [isOpen, setIsOpen] = createSignal(false);

  const selectedOption = () => props.options.find(o => o.value === props.value);

  const handleSelect = (value: string) => {
    props.onChange(value);
    setIsOpen(false);
  };

  return (
    <div class="dropdown">
      <button
        class="dropdown-trigger"
        onClick={() => setIsOpen(!isOpen())}
        onBlur={() => setTimeout(() => setIsOpen(false), 150)}
      >
        <span class="dropdown-value">{selectedOption()?.label || props.value}</span>
        <svg class="dropdown-arrow" width="12" height="12" viewBox="0 0 12 12" fill="currentColor">
          <path d="M2.5 4.5L6 8L9.5 4.5" stroke="currentColor" stroke-width="1.5" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </button>
      <Show when={isOpen()}>
        <div class="dropdown-menu">
          <For each={props.options}>
            {(option) => (
              <button
                class={`dropdown-item ${option.value === props.value ? 'selected' : ''}`}
                onClick={() => handleSelect(option.value)}
              >
                {option.label}
                <Show when={option.value === props.value}>
                  <svg class="dropdown-check" width="12" height="12" viewBox="0 0 12 12" fill="currentColor">
                    <path d="M2 6L5 9L10 3" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
                  </svg>
                </Show>
              </button>
            )}
          </For>
        </div>
      </Show>
    </div>
  );
};

export default Dropdown;
