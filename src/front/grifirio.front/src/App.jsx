import Nav from './components/Nav';
import Hero from './components/Hero';
import LogoStrip from './components/LogoStrip';
import HowItWorks from './components/HowItWorks';
import ChatAnalysis from './components/ChatAnalysis';
import FeatureGrid from './components/FeatureGrid';
import Pricing from './components/Pricing';
import Faq from './components/Faq';
import CtaBand from './components/CtaBand';
import Footer from './components/Footer';

export default function App() {
  return (
    <>
      <Nav />
      <main>
        <Hero />
        <LogoStrip />
        <HowItWorks />
        <ChatAnalysis />
        <FeatureGrid />
        <Pricing />
        <Faq />
        <CtaBand />
      </main>
      <Footer />
    </>
  );
}
