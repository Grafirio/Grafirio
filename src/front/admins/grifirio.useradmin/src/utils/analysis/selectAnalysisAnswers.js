export default function selectAnalysisAnswers(questions, answers) {
  return Object.fromEntries(questions
    .filter(question => typeof answers[question.id] === 'string' && answers[question.id].trim() !== '')
    .map(question => [question.id, answers[question.id]]));
}