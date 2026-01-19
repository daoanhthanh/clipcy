import { Component } from 'solid-js';

interface TextInputProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}

const TextInput: Component<TextInputProps> = (props) => {
  return (
    <input
      type="text"
      class="text-input"
      value={props.value}
      placeholder={props.placeholder}
      onChange={(e) => props.onChange(e.currentTarget.value)}
    />
  );
};

export default TextInput;
