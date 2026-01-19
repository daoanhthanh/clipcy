import { Component } from 'solid-js';

interface NumberInputProps {
  value: number;
  min?: number;
  max?: number;
  onChange: (value: number) => void;
}

const NumberInput: Component<NumberInputProps> = (props) => {
  const handleChange = (e: Event) => {
    const target = e.currentTarget as HTMLInputElement;
    let value = parseInt(target.value, 10);

    if (isNaN(value)) {
      value = props.min ?? 0;
    }

    if (props.min !== undefined && value < props.min) {
      value = props.min;
    }

    if (props.max !== undefined && value > props.max) {
      value = props.max;
    }

    props.onChange(value);
  };

  return (
    <div class="number-input">
      <input
        type="number"
        value={props.value}
        min={props.min}
        max={props.max}
        onChange={handleChange}
      />
    </div>
  );
};

export default NumberInput;
